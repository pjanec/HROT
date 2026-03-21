using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Map.Common;
using Bagira.Map.Common.Dds;
using CycloneDDS.Runtime;
using FDP.Toolkit.DER;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the IOS (Interactive Operations Station).
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates <see cref="DerRepo"/>, all IOS panels,
    ///   <see cref="IosLogic"/>, and <see cref="IosMock"/>.</item>
    ///   <item><see cref="Update"/> — delegates to <see cref="IosMock.Update"/>.</item>
    ///   <item><see cref="DrawWorld"/> — no-op (IOS has no 3-D world visuals; all UI is ImGui).</item>
    ///   <item><see cref="DrawUI"/> — delegates to <see cref="IosMock.DrawUI"/>
    ///   (rendered inside <c>rlImGui.Begin()</c>).
    ///   Skipped when <see cref="SubsystemConfig.Headless"/> is <c>true</c>.</item>
    ///   <item><see cref="Shutdown"/> — disposes <see cref="IosMock"/> and underlying logic.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class IosSubsystem : ISubsystem
    {
        /// <inheritdoc/>
        public string Name => "IOS";

        /// <inheritdoc/>
        /// <remarks>Violet — distinct from IG (green) and SimHost (red).</remarks>
        public System.Numerics.Vector4 TitleBarColor =>
            new System.Numerics.Vector4(0.32f, 0.08f, 0.48f, 1f);

        // Map group 0 = broadcast to all IG instances (matches IosLogicConstants.DefaultMapGroupId).
        private const int DefaultMapGroupId = 0;

        // DDS topic names — must match IosLogicConstants and IG/SimHost readers.
        private const string TopicMapConfig       = "MapInteractionConfig";
        private const string TopicCreateEntity    = "CreateEntityRequest";
        private const string TopicMissionControl  = "MissionControlRequest";
        private const string TopicContextActions  = "ContextActionsUpdate";
        private const string TopicMapCommand      = "MapCommandRequest";
        private const string TopicMapCommandAck   = "MapCommandAck";

        /// <summary>MapId of the IG this IOS issues tool-activation commands to (300 = default IG instance).</summary>
        private const int TargetMapId = 300;

        private IosMock?         _mock;
        private bool             _headless;
        private DdsParticipant?  _participant;
        private List<IDisposable>? _ingressDisposables;
        private int              _nodeIdOverride;

        /// <summary>
        /// Internal test hook for integration tests.
        /// </summary>
        internal IosLogic Logic => _mock?.Logic ?? throw new InvalidOperationException("Not initialized");

        /// <summary>
        /// Internal test hook: returns the effective NodeId wired from <see cref="SubsystemConfig.NodeId"/>
        /// at initialization time.
        /// </summary>
        internal int TestHook_NodeIdOverride => _nodeIdOverride;

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            _nodeIdOverride = config.NodeId;

            // ── DDS participant ────────────────────────────────────────────────
            _participant = BagiraEnvironment.CreateParticipant(config.DomainId);

            // ── Construct services ─────────────────────────────────────────────
            // DerRepo takes no external dependencies; node ID uses a fixed default.
            var repo              = new DerRepo();
            var transactionMgr    = new RequestTransactionManager();
            var interactionPanel  = new InteractionPanel();

            var clickQueue           = new ConcurrentEventQueue<MapClickEvent>();
            var selectionQueue       = new ConcurrentEventQueue<SelectionChangedEvent>();
            var missionAckQueue      = new ConcurrentEventQueue<MissionControlAck>();
            var createEntityAckQueue = new ConcurrentEventQueue<CreateEntityAck>();
            var mapCommandAckQueue   = new ConcurrentEventQueue<MapCommandAck>();

            // DDS ingress handlers for click/selection events.
            var ingressHandlers = new List<IIngressHandler>
            {
                new MapClickIngressHandler(_participant, clickQueue),
                new SelectionChangedIngressHandler(_participant, selectionQueue),
                new MissionControlAckIngressHandler(_participant, missionAckQueue),
                new CreateEntityAckIngressHandler(_participant, createEntityAckQueue),
                new MapCommandAckIngressHandler(_participant, mapCommandAckQueue),
                new MasterIngressHandler<EntityMaster>(
                    _participant,
                    repo,
                    "EntityMaster",
                    master => master.EntityId,
                    master => master.TkbType),
                // Descriptor handlers — populate the DER repo with all descriptor types
                // so the IOS Entity Inspector can show the full entity state.
                new DescriptorIngressHandler<GeoSpatial>(
                    _participant, repo, "GeoSpatial",    d => d.EntityId),
                new DescriptorIngressHandler<EntityInfo>(
                    _participant, repo, "EntityInfo",    d => d.EntityId),
                new DescriptorIngressHandler<EntityDamage>(
                    _participant, repo, "EntityDamage",  d => d.EntityId),
                new DescriptorIngressHandler<MapVisualOverlay>(
                    _participant, repo, "MapVisualOverlay", d => d.EntityId),
            };

            _ingressDisposables = new List<IDisposable>(ingressHandlers.Count);
            for (int i = 0; i < ingressHandlers.Count; i++)
            {
                if (ingressHandlers[i] is IDisposable disposable)
                    _ingressDisposables.Add(disposable);
            }

            // Live DDS writers — publish IOS state changes to the network.
            var configWriter       = new DdsWriterAdapter<MapInteractionConfig>(_participant, TopicMapConfig);
            var createEntityWriter = new DdsWriterAdapter<CreateEntityRequest>(_participant, TopicCreateEntity);
            var missionCmdWriter   = new DdsWriterAdapter<MissionControlRequest>(_participant, TopicMissionControl);
            var contextMenuWriter  = new DdsWriterAdapter<ContextActionsUpdate>(_participant, TopicContextActions);
            var commandWriter      = new DdsWriterAdapter<MapCommandRequest>(_participant, TopicMapCommand);

            var missionEditorSvc = new MissionEditorService(repo, missionCmdWriter, ackQueue: missionAckQueue);
            var contextMenuLogic  = new ContextMenuLogic(repo, contextMenuWriter);

            // MissionEditorService doubles as an IIngressHandler: its Poll() drains
            // the ack queue and resolves pending CommitMissionAsync tasks.
            ingressHandlers.Add(missionEditorSvc);

            var logic = new IosLogic(
                repo:                 repo,
                missionEditorService: missionEditorSvc,
                contextMenuLogic:     contextMenuLogic,
                transactionManager:   transactionMgr,
                configWriter:         configWriter,
                createEntityWriter:   createEntityWriter,
                clickQueue:           clickQueue,
                selectionQueue:       selectionQueue,
                interactionPanel:     interactionPanel,
                ingressHandlers:         ingressHandlers,
                createEntityAckQueue:    createEntityAckQueue,
                mapGroupId:              DefaultMapGroupId,
                commandWriter:        commandWriter,
                targetMapId:          TargetMapId,
                mapCommandAckQueue:   mapCommandAckQueue);

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

            _mock = new IosMock(
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
            _mock?.Update(deltaTime);
        }

        /// <summary>No-op — IOS has no 3-D world visuals; all content is rendered via <see cref="DrawUI"/>.</summary>
        public void DrawWorld() { }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders all IOS ImGui panels (config, orbat, mission, interaction, spawner).
        /// Called inside <c>rlImGui.Begin()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawUI()
        {
            if (!_headless)
                _mock?.DrawUI();
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
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
