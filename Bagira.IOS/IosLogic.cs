using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Map.Common.Dds;
using FDP.Kernel.Logging;
using FDP.Toolkit.DER;
using Newtonsoft.Json;

namespace Bagira.IOS;

/// <summary>
/// Core application-state and network-traffic-cop for the IOS Mock.
///
/// <para>Implements <see cref="IIosLogic"/> so that all UI panels depend only
/// on the interface, keeping them testable in isolation.</para>
///
/// <para><b>Threading model:</b>
/// <list type="bullet">
///   <item><see cref="Update"/> must be called from the main (Raylib) thread
///   exactly once per frame.  It polls all registered ingress handlers,
///   drains the interaction-log queue (IOS-DEBT-034), processes buffered DDS
///   events, and checks request timeouts.</item>
///   <item>All DDS writers used internally are called only from
///   <see cref="Update"/> or the synchronous command-handler methods
///   (<see cref="SendConfigPatch"/>, <see cref="StartPlacementMode"/>), which
///   are always invoked from the main thread via ImGui button callbacks.</item>
///   <item><see cref="InteractionPanel.AddLog"/> enqueues to a
///   <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/> so DDS
///   ingress adapters may call it freely from background threads.</item>
/// </list>
/// </para>
/// </summary>
public sealed class IosLogic : IIosLogic, IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IDerRepo Repo { get; }

    /// <inheritdoc/>
    public IMissionEditorService MissionEditorService { get; }

    /// <summary>Context-menu strategy manager; driven by selection-change events.</summary>
    public IContextMenuLogic ContextMenuLogic { get; }

    /// <summary>In-flight request tracker for all outgoing DDS requests.</summary>
    public IRequestTransactionManager TransactionManager { get; }

    private readonly IDdsWriter<MapInteractionConfig>  _configWriter;
    private readonly IDdsWriter<CreateEntityRequest>   _createEntityWriter;
    private readonly IEventQueue<MapClickEvent>         _clickQueue;
    private readonly IEventQueue<SelectionChangedEvent> _selectionQueue;
    private readonly InteractionPanel                   _interactionPanel;
    private readonly List<IIngressHandler>              _ingressHandlers;
    private readonly int                                _mapGroupId;

    // ── Mutable state ─────────────────────────────────────────────────────────

    /// <summary>
    /// The context ID that was embedded in the most recent
    /// <see cref="MapInteractionConfig"/> published by this node.
    /// Incoming <see cref="MapClickEvent"/> samples are only processed when
    /// their <c>InteractionContextId</c> matches this value; all others are
    /// silently dropped as stale.
    /// <see cref="Guid.Empty"/> when no placement mode is active.
    /// </summary>
    public Guid ActiveContextId { get; private set; } = Guid.Empty;

    /// <summary>TKB type requested for the next entity placement. 0 = none.</summary>
    public long PlacementType { get; private set; }

    /// <summary>Entity ID currently highlighted/selected in the UI panels. 0 = none.</summary>
    public int SelectedEntityId { get; private set; }

    /// <summary>
    /// True if <see cref="OpenSpawner"/> was called since the last
    /// <see cref="ConsumeSpawnerRequest"/>.
    /// </summary>
    public bool SpawnerRequested { get; private set; }

    private bool _disposed;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="IosLogic"/> with all dependencies injected.
    /// All non-optional parameters are validated for null.
    /// </summary>
    /// <param name="repo">DER entity repository (read by panels).</param>
    /// <param name="missionEditorService">Service for mission plan operations.</param>
    /// <param name="contextMenuLogic">Context-menu push service.</param>
    /// <param name="transactionManager">In-flight request correlator.</param>
    /// <param name="configWriter">Writer for <see cref="MapInteractionConfig"/> messages.</param>
    /// <param name="createEntityWriter">Writer for <see cref="CreateEntityRequest"/> messages.</param>
    /// <param name="clickQueue">Pull queue for incoming <see cref="MapClickEvent"/> samples.</param>
    /// <param name="selectionQueue">Pull queue for incoming <see cref="SelectionChangedEvent"/> samples.</param>
    /// <param name="interactionPanel">Event-log panel (also the DEBT-034 drain target).</param>
    /// <param name="ingressHandlers">
    /// Optional list of DDS ingress handlers to <see cref="IIngressHandler.Poll"/> each frame.
    /// Typically includes one <c>MasterIngressHandler</c> and several
    /// <c>DescriptorIngressHandler</c> instances feeding the DER repo.
    /// Pass <c>null</c> or empty in unit tests.
    /// </param>
    /// <param name="mapGroupId">Map group targeted by config publications.</param>
    public IosLogic(
        IDerRepo                            repo,
        IMissionEditorService               missionEditorService,
        IContextMenuLogic                   contextMenuLogic,
        IRequestTransactionManager          transactionManager,
        IDdsWriter<MapInteractionConfig>    configWriter,
        IDdsWriter<CreateEntityRequest>     createEntityWriter,
        IEventQueue<MapClickEvent>          clickQueue,
        IEventQueue<SelectionChangedEvent>  selectionQueue,
        InteractionPanel                    interactionPanel,
        IEnumerable<IIngressHandler>?       ingressHandlers = null,
        int                                 mapGroupId      = IosLogicConstants.DefaultMapGroupId)
    {
        Repo                 = repo                 ?? throw new ArgumentNullException(nameof(repo));
        MissionEditorService = missionEditorService ?? throw new ArgumentNullException(nameof(missionEditorService));
        ContextMenuLogic     = contextMenuLogic     ?? throw new ArgumentNullException(nameof(contextMenuLogic));
        TransactionManager   = transactionManager   ?? throw new ArgumentNullException(nameof(transactionManager));
        _configWriter        = configWriter         ?? throw new ArgumentNullException(nameof(configWriter));
        _createEntityWriter  = createEntityWriter   ?? throw new ArgumentNullException(nameof(createEntityWriter));
        _clickQueue          = clickQueue           ?? throw new ArgumentNullException(nameof(clickQueue));
        _selectionQueue      = selectionQueue       ?? throw new ArgumentNullException(nameof(selectionQueue));
        _interactionPanel    = interactionPanel     ?? throw new ArgumentNullException(nameof(interactionPanel));
        _ingressHandlers     = ingressHandlers?.ToList() ?? new List<IIngressHandler>();
        _mapGroupId          = mapGroupId;
    }

    // ── IIosLogic ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void SendConfigPatch(string jsonPatch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(jsonPatch);

        _configWriter.Write(new MapInteractionConfig
        {
            MapGroupId          = _mapGroupId,
            ActiveContextId     = ActiveContextId,
            JsonSchemaVersion   = IosLogicConstants.JsonSchemaVersion,
            ConfigurationJson   = jsonPatch
        });

        _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicConfig,
            $"patch={jsonPatch.Length}ch");
    }

    /// <inheritdoc/>
    public void StartPlacementMode(long tkbType, eForceIdentifier affiliation)
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = tkbType;

        string patch = BuildPlacementPatch(tkbType, affiliation);

        _configWriter.Write(new MapInteractionConfig
        {
            MapGroupId          = _mapGroupId,
            ActiveContextId     = ActiveContextId,
            JsonSchemaVersion   = IosLogicConstants.JsonSchemaVersion,
            ConfigurationJson   = patch
        });

        FdpLog<IosLogic>.Debug(
            "[TRACE-IOS] Placement Mode ON. ContextId={0} TKB={1}", ActiveContextId, tkbType);

        _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicConfig,
            $"PLACEMENT tkb={tkbType} ctx={ActiveContextId:N}");
    }

    /// <inheritdoc/>
    public void SelectEntity(int entityId)
    {
        ThrowIfDisposed();
        SelectedEntityId = entityId;
    }

    /// <inheritdoc/>
    public void OpenSpawner()
    {
        ThrowIfDisposed();
        SpawnerRequested = true;
    }

    /// <summary>
    /// Resets the spawner-requested flag after the UI shell has acted on it.
    /// Call from <see cref="IosMock.Update"/> after forwarding the flag to the
    /// spawner panel.
    /// </summary>
    public void ConsumeSpawnerRequest() => SpawnerRequested = false;

    // ── Main update ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called once per frame from the application shell (main thread).
    ///
    /// <para>Execution order:
    /// <list type="number">
    ///   <item>Poll all registered DDS ingress handlers → feeds the DER repo.</item>
    ///   <item>Drain the interaction-log staging queue (IOS-DEBT-034).</item>
    ///   <item>Process buffered <see cref="MapClickEvent"/> samples.</item>
    ///   <item>Process buffered <see cref="SelectionChangedEvent"/> samples.</item>
    ///   <item>Check pending-request timeouts.</item>
    /// </list>
    /// </para>
    /// </summary>
    public void Update()
    {
        ThrowIfDisposed();

        // 1. Network ingress – drain all registered DDS ingress handlers
        for (int i = 0; i < _ingressHandlers.Count; i++)
            _ingressHandlers[i].Poll();

        // 2. Drain pending log entries onto the main thread (IOS-DEBT-034)
        _interactionPanel.DrainPendingLogs();

        // 3. Process event queues
        ProcessClickEvents();
        ProcessSelectionEvents();

        // 4. Check request timeouts
        TransactionManager.CheckTimeouts();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ProcessClickEvents()
    {
        while (_clickQueue.TryDequeue(out var evt))
        {
            FdpLog<IosLogic>.Debug(
                "[TRACE-IOS] MapClickEvent ContextId={0} (expected {1})",
                evt.InteractionContextId,
                ActiveContextId);

            // Drop stale clicks: context ID must match the one we published.
            if (evt.InteractionContextId != ActiveContextId)
            {
                _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicClick,
                    $"DROP ctx={evt.InteractionContextId:N} (expected {ActiveContextId:N})");
                continue;
            }

            // Drop clicks when no placement type has been configured.
            if (PlacementType == 0)
            {
                _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicClick,
                    "DROP – no placement type configured");
                continue;
            }

            // Track the outgoing request.
            var requestId = Guid.NewGuid();
            TransactionManager.TrackRequest(requestId,
                $"Create entity tkbType={PlacementType}");

            _createEntityWriter.Write(new CreateEntityRequest
            {
                RequestId          = requestId,
                Owner              = new NodeId { AppDomainId = 0, AppInstanceId = 0 },
                Flags              = 0,
                InitialDescriptors = BuildInitialDescriptors(evt.Position)
            });

            _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicCreate,
                $"tkb={PlacementType} pos={evt.Position.Latitude:F2},{evt.Position.Longitude:F2}");
        }
    }

    private void ProcessSelectionEvents()
    {
        while (_selectionQueue.TryDequeue(out var evt))
        {
            ContextMenuLogic.OnSelectionChanged(evt);
            _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicSelection,
                $"{evt.SelectedEntityIds?.Count ?? 0} entities");
        }
    }

    /// <summary>
    /// Builds the minimal initial-descriptor list for a new entity created at
    /// <paramref name="position"/> with the current <see cref="PlacementType"/>.
    /// </summary>
    private List<EntityDescriptorUnion> BuildInitialDescriptors(GeoPosition position)
    {
        return new List<EntityDescriptorUnion>
        {
            new EntityDescriptorUnion
            {
                _d           = EDescriptorType.dtEntityMaster,
                EntityMaster = new EntityMaster
                {
                    EntityId = -1,
                    TkbType = PlacementType
                }
            },
            new EntityDescriptorUnion
            {
                _d         = EDescriptorType.dtGeoSpatial,
                GeoSpatial = new GeoSpatial { Pos = position }
            }
        };
    }

    /// <summary>
    /// Builds the JSON config patch that activates the placement tool.
    /// </summary>
    private static string BuildPlacementPatch(long tkbType, eForceIdentifier affiliation)
    {
        return JsonConvert.SerializeObject(new
        {
            interaction = new
            {
                activeTool = IosLogicConstants.PlacementToolName,
                toolConfig = new
                {
                    entityType  = tkbType,
                    affiliation = affiliation.ToString()
                }
            }
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IosLogic));
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>Marks the instance as disposed; idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
