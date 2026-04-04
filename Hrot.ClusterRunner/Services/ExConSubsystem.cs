using Hrot.NED.Descriptors;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.NED.Messages;
using Hrot.Common.Orchestration;
using Hrot.ExCon;
using Hrot.ExCon.Logic;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Hrot.Map.Common;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using FDP.Toolkit.DER;
using Hrot.ClusterRunner.Windows;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Core.Time;

namespace Hrot.ClusterRunner.Services
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

        // DDS topic names — must match ExConLogicConstants and IG/SimHost readers.
        private const string TopicMapConfig       = "MapInteractionConfig";
        private const string TopicCreateEntity    = "CreateEntityRequest";
        private const string TopicDeleteEntity    = "DeleteEntityRequest";
        private const string TopicMissionControl  = "MissionControlRequest";
        private const string TopicContextActions  = "ContextActionsUpdate";
        private const string TopicMapCommand      = "MapCommandRequest";
        private const string TopicMapCommandAck   = "MapCommandAck";

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
        // ── PACK-E002: Mission egress/ingress bus ─────────────────────────────────────
        private FdpEventBus?                             _missionBus;
        private MissionControlEgressTranslator?          _missionEgressTranslator;
        private MissionControlAckIngressTranslator?      _missionAckIngressTranslator;
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

            var clickQueue                   = new ConcurrentEventQueue<MapClickEvent>();
            var selectionQueue               = new ConcurrentEventQueue<SelectionChangedEvent>();
            var createUpdateDeleteEntityAckQueue = new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>();
            var mapCommandAckQueue           = new ConcurrentEventQueue<MapCommandAck>();

            // DDS ingress handlers for click/selection events.
            var ingressHandlers = new List<IIngressHandler>
            {
                new MapClickIngressHandler(_participant, clickQueue),
                new SelectionChangedIngressHandler(_participant, selectionQueue),
                new CreateUpdateDeleteEntityAckIngressHandler(_participant, createUpdateDeleteEntityAckQueue),
                new MapCommandAckIngressHandler(_participant, mapCommandAckQueue),
                new MasterIngressHandler<EntityMaster>(
                    _participant,
                    repo,
                    "EntityMaster",
                    master => master.EntityId,
                    master => master.TkbType),
                // Descriptor handlers — populate the DER repo with all descriptor types
                // so the ExCon Entity Inspector can show the full entity state.
                new DescriptorIngressHandler<WorldPos>(
                    _participant, repo, "GeoSpatial",    d => d.EntityId),
                new DescriptorIngressHandler<Hrot.NED.Descriptors.EntityInfo>(
                    _participant, repo, "EntityInfo",    d => d.EntityId),
                new DescriptorIngressHandler<EntityDamage>(
                    _participant, repo, "EntityDamage",  d => d.EntityId),
                new DescriptorIngressHandler<MapVisualOverlay>(
                    _participant, repo, "MapVisualOverlay", d => d.EntityId),
                new DescriptorIngressHandler<MapRoute>(
                    _participant, repo, "MapRoute", d => d.EntityId),
            };

            _ingressDisposables = new List<IDisposable>(ingressHandlers.Count);
            for (int i = 0; i < ingressHandlers.Count; i++)
            {
                if (ingressHandlers[i] is IDisposable disposable)
                    _ingressDisposables.Add(disposable);
            }

            // Live DDS writers — publish ExCon state changes to the network.
            var configWriter       = new DdsWriterAdapter<MapInteractionConfig>(_participant, TopicMapConfig);
            var createEntityWriter = new DdsWriterAdapter<CreateEntityRequest>(_participant, TopicCreateEntity);
            var deleteEntityWriter = new DdsWriterAdapter<Hrot.NED.Messages.DeleteEntityRequest>(_participant, TopicDeleteEntity);
            var contextMenuWriter  = new DdsWriterAdapter<ContextActionsUpdate>(_participant, TopicContextActions);
            var commandWriter      = new DdsWriterAdapter<MapCommandRequest>(_participant, TopicMapCommand);

            // PACK-E002: mission bus replaces DDS writer + ACK queue for MissionEditorService
            _missionBus                  = new FdpEventBus();
            _missionEgressTranslator     = new MissionControlEgressTranslator(_missionBus, _participant);
            _missionAckIngressTranslator = new MissionControlAckIngressTranslator(_participant, _missionBus);
            var missionEditorSvc = new MissionEditorService(repo, _missionBus);
            var contextMenuLogic  = new ContextMenuLogic(repo, contextMenuWriter);

            // MissionEditorService still implements IIngressHandler via Poll() which
            // drains MissionControlAckEvent from the bus.
            ingressHandlers.Add(missionEditorSvc);

            // ── Cluster control wiring (S0507 / PACK-C002) ────────────────────────────
            var iosLogicSysOpWriter = new DdsWriterAdapter<ClusterOpRequest>(_participant, "ClusterOpRequest");
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
                configWriter:         configWriter,
                createEntityWriter:   createEntityWriter,
                clickQueue:           clickQueue,
                selectionQueue:       selectionQueue,
                interactionPanel:             interactionPanel,
                ingressHandlers:              ingressHandlers,
                createEntityAckQueue:         createUpdateDeleteEntityAckQueue,
                mapGroupId:                   DefaultMapGroupId,
                commandWriter:        commandWriter,
                targetMapId:          TargetMapId,
                mapCommandAckQueue:   mapCommandAckQueue,
                deleteEntityWriter:   deleteEntityWriter,
                sysOpWriter:          iosLogicSysOpWriter);

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
                configPanel:      new ConfigPanel(),
                orbatPanel:       new OrbatPanel(orbatCatalog),
                missionPanel:     new MissionPanel(),
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

            // PACK-E002: mission bus pipeline
            // 1. Pull DDS ACKs into write buffer; 2. Swap so ACKs become readable;
            // 3. Flush intents to DDS; 4. Mock.Update() → Poll() drains ACKs from read buffer.
            _missionAckIngressTranslator?.Tick();  // DDS ACK → _missionBus write buf
            _missionBus?.SwapBuffers();             // write → read (intents and ACKs visible)
            _missionEgressTranslator?.Tick();       // read MissionControlIntent → DDS
            _mock?.Update(deltaTime);               // ExConLogic.Update → Poll() reads ACKs
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
                windowManager.RegisterWindow(new ExConMissionWindow(_mock.GetMissionPanel(), logic));
                windowManager.RegisterWindow(new ExConDataMonitorWindow(_mock.GetInteractionPanel(), logic));
                windowManager.RegisterWindow(new ExConSpawnerWindow(_mock.GetSpawnerPanel(), logic));
                windowManager.RegisterWindow(new ExConConfigWindow(_mock.GetConfigPanel(), logic));
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
            _missionEgressTranslator?.Dispose();
            _missionEgressTranslator = null;
            _missionAckIngressTranslator?.Dispose();
            _missionAckIngressTranslator = null;
            _missionBus = null;
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
}
