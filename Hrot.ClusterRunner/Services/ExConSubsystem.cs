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

        // ── S0507: Cluster control ─────────────────────────────────────────────
        private DdsWriter<ClusterOpRequest>?          _sysOpWriter;
        private DdsWriterAdapter<ClusterOpRequest>?   _iosLogicSysOpWriter;
        private ClusterUiCache?                   _uiCache;
        private ClusterScenarioPanel?             _clusterPanel;
        private TimePulseIngressHandler?          _timePulseHandler;
        private TimeModeIngressHandler?           _timeModeHandler;

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

            // ── ClusterSlave (CGF1-S0104) ────────────────────────────────────────
            var iosNodeId  = config.NodeId != 0 ? config.NodeId : 500;
            var iosTransport = new DdsOrchestrationTransport(_participant, iosNodeId);
            _clusterSlave = new FDP.Toolkit.Orchestration.ClusterSlave(iosTransport, iosNodeId, "ExCon");

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
                iosTransport,
                iosNodeId,
                storageDirectory:      @"C:\FDP_Temp"));

            // Wire ReferenceLiveLoadHandler: ACKs cold PrepareLive and FinalizeLive.
            // ExCon carries no ECS state and does not start a recording.
            _clusterSlave.RegisterHandler(new FDP.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(
                checkpointWorker: null,
                controller:       iosRrController,
                storageDirectory: @"C:\FDP_Temp",
                transport:        iosTransport,
                nodeId:           iosNodeId));

            // CGF1-S0309: wire dry-run snapshot/rewind handler (ExCon carries no ECS state).
            _clusterSlave.RegisterHandler(new ReferencePreviewHandler(liveRepo: null));

            // ── Construct services ─────────────────────────────────────────────
            // DerRepo takes no external dependencies; node ID uses a fixed default.
            var repo              = new DerRepo();
            var transactionMgr    = new RequestTransactionManager();
            var interactionPanel  = new InteractionPanel();

            var clickQueue                   = new ConcurrentEventQueue<MapClickEvent>();
            var selectionQueue               = new ConcurrentEventQueue<SelectionChangedEvent>();
            var missionAckQueue              = new ConcurrentEventQueue<MissionControlAck>();
            var createUpdateDeleteEntityAckQueue = new ConcurrentEventQueue<CreateUpdateDeleteEntityAck>();
            var mapCommandAckQueue           = new ConcurrentEventQueue<MapCommandAck>();

            // DDS ingress handlers for click/selection events.
            var ingressHandlers = new List<IIngressHandler>
            {
                new MapClickIngressHandler(_participant, clickQueue),
                new SelectionChangedIngressHandler(_participant, selectionQueue),
                new MissionControlAckIngressHandler(_participant, missionAckQueue),
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
            var missionCmdWriter   = new DdsWriterAdapter<MissionControlRequest>(_participant, TopicMissionControl);
            var contextMenuWriter  = new DdsWriterAdapter<ContextActionsUpdate>(_participant, TopicContextActions);
            var commandWriter      = new DdsWriterAdapter<MapCommandRequest>(_participant, TopicMapCommand);

            var missionEditorSvc = new MissionEditorService(repo, missionCmdWriter, ackQueue: missionAckQueue);
            var contextMenuLogic  = new ContextMenuLogic(repo, contextMenuWriter);

            // MissionEditorService doubles as an IIngressHandler: its Poll() drains
            // the ack queue and resolves pending CommitMissionAsync tasks.
            ingressHandlers.Add(missionEditorSvc);

            // ── Cluster control wiring (S0507) ─────────────────────────────────────────
            _sysOpWriter         = new DdsWriter<ClusterOpRequest>(_participant);
            _iosLogicSysOpWriter = new DdsWriterAdapter<ClusterOpRequest>(_participant, "ClusterOpRequest");
            _uiCache      = new ClusterUiCache(_participant);
            _clusterPanel = new ClusterScenarioPanel(_sysOpWriter, _uiCache);

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
                sysOpWriter:          _iosLogicSysOpWriter);

            // S0507: Time ingress — must be constructed after `logic` to capture the callback
            _timePulseHandler = new TimePulseIngressHandler(_participant, pulse => logic.OnTimePulse(pulse));
            _timeModeHandler  = new TimeModeIngressHandler(_participant, mode  => logic.OnTimeMode(mode));
            _ingressDisposables!.Add(_timePulseHandler);
            _ingressDisposables!.Add(_timeModeHandler);

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
            _clusterSlave?.Tick();
            _uiCache?.Update();
            _timePulseHandler?.Poll();
            _timeModeHandler?.Poll();
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
            _iosLogicSysOpWriter?.Dispose();
            _iosLogicSysOpWriter = null;
            _sysOpWriter?.Dispose();
            _sysOpWriter = null;
            _mock?.Dispose();
            _mock = null;
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
