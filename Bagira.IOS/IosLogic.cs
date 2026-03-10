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
/// <summary>Tracks which kind of interactive pick is currently awaited from the IG.</summary>
public enum IosPickMode { None, EntityCreation, Location, Entity }

public sealed class IosLogic : IIosLogic, IMapPickService, IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IDerRepo Repo { get; }

    /// <inheritdoc/>
    public IMissionEditorService MissionEditorService { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="IosLogic"/> implements <see cref="IMapPickService"/> directly.
    /// </remarks>
    public IMapPickService MapPickService => this;

    /// <summary>Context-menu strategy manager; driven by selection-change events.</summary>
    public IContextMenuLogic ContextMenuLogic { get; }

    /// <summary>In-flight request tracker for all outgoing DDS requests.</summary>
    public IRequestTransactionManager TransactionManager { get; }

    private readonly IDdsWriter<MapInteractionConfig>  _configWriter;
    private readonly IDdsWriter<CreateEntityRequest>   _createEntityWriter;
    private readonly IDdsWriter<MapCommandRequest>?    _commandWriter;
    private readonly IEventQueue<MapClickEvent>         _clickQueue;
    private readonly IEventQueue<SelectionChangedEvent> _selectionQueue;
    private readonly InteractionPanel                   _interactionPanel;
    private readonly List<IIngressHandler>              _ingressHandlers;
    private readonly int                                _mapGroupId;
    /// <summary>MapId of the IG instance that receives tool-activation commands (0 = broadcast).</summary>
    private readonly int                                _targetMapId;

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

    // ── Map pick state ─────────────────────────────────────────────────────────

    /// <summary>Tracks what kind of pending pick is awaited from the IG.</summary>
    public IosPickMode PickMode { get; private set; } = IosPickMode.None;

    private TaskCompletionSource<GeoPosition>? _pendingLocationTcs;
    private TaskCompletionSource<int>?         _pendingEntityTcs;

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
    /// <param name="commandWriter">Optional writer for <see cref="MapCommandRequest"/> messages used for tool activation.</param>
    /// <param name="targetMapId">
    /// Target IG MapId for tool-activation commands.  Use <c>0</c> to broadcast to all IGs in the group.
    /// Defaults to <see cref="IosLogicConstants.DefaultTargetMapId"/>.
    /// </param>
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
        int                                 mapGroupId      = IosLogicConstants.DefaultMapGroupId,
        IDdsWriter<MapCommandRequest>?      commandWriter   = null,
        int                                 targetMapId     = IosLogicConstants.DefaultTargetMapId)
    {
        Repo                 = repo                 ?? throw new ArgumentNullException(nameof(repo));
        MissionEditorService = missionEditorService ?? throw new ArgumentNullException(nameof(missionEditorService));
        ContextMenuLogic     = contextMenuLogic     ?? throw new ArgumentNullException(nameof(contextMenuLogic));
        TransactionManager   = transactionManager   ?? throw new ArgumentNullException(nameof(transactionManager));
        _configWriter        = configWriter         ?? throw new ArgumentNullException(nameof(configWriter));
        _createEntityWriter  = createEntityWriter   ?? throw new ArgumentNullException(nameof(createEntityWriter));
        _commandWriter       = commandWriter;   // null-ok; falls back to MapInteractionConfig
        _clickQueue          = clickQueue           ?? throw new ArgumentNullException(nameof(clickQueue));
        _selectionQueue      = selectionQueue       ?? throw new ArgumentNullException(nameof(selectionQueue));
        _interactionPanel    = interactionPanel     ?? throw new ArgumentNullException(nameof(interactionPanel));
        _ingressHandlers     = ingressHandlers?.ToList() ?? new List<IIngressHandler>();
        _mapGroupId          = mapGroupId;
        _targetMapId         = targetMapId;
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
        PickMode        = IosPickMode.EntityCreation;
        CancelPendingPick();

        if (_commandWriter != null)
        {
            // Preferred path: instance-scoped volatile command (correct architecture)
            string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                contextId   = ActiveContextId.ToString("N"),
                entityType  = tkbType,
                affiliation = affiliation.ToString()
            });
            _commandWriter.Write(new MapCommandRequest
            {
                RequestId       = Guid.NewGuid(),
                MapId           = _targetMapId,
                Type            = CommandType.CMD_PLACE_ENTITY,
                CommandArgsJson = argsJson,
            });
            _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicCommand,
                $"CMD_PLACE_ENTITY tkb={tkbType} ctx={ActiveContextId:N}");
        }
        else
        {
            // Fallback: legacy MapInteractionConfig (group-scoped, transient-local)
            string patch = BuildPlacementPatch(tkbType, affiliation);
            _configWriter.Write(new MapInteractionConfig
            {
                MapGroupId        = _mapGroupId,
                ActiveContextId   = ActiveContextId,
                JsonSchemaVersion = IosLogicConstants.JsonSchemaVersion,
                ConfigurationJson = patch
            });
            _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicConfig,
                $"PLACEMENT tkb={tkbType} ctx={ActiveContextId:N}");
        }

        FdpLog<IosLogic>.Debug(
            "[TRACE-IOS] Placement Mode ON. ContextId={0} TKB={1}", ActiveContextId, tkbType);
    }

    /// <inheritdoc/>
    public void StartAreaAuthoringMode(string styleOverrideJson = "")
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;

        if (_commandWriter != null)
        {
            // Preferred path: instance-scoped volatile command (correct architecture)
            string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                contextId        = ActiveContextId.ToString("N"),
                styleOverrideJson
            });
            _commandWriter.Write(new MapCommandRequest
            {
                RequestId       = Guid.NewGuid(),
                MapId           = _targetMapId,
                Type            = CommandType.CMD_START_AUTHORING,
                CommandArgsJson = argsJson,
            });
            _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicCommand,
                $"CMD_START_AUTHORING ctx={ActiveContextId:N}");
        }
        else
        {
            // Fallback: legacy MapInteractionConfig
            string patch = BuildAreaAuthoringPatch(styleOverrideJson);
            _configWriter.Write(new MapInteractionConfig
            {
                MapGroupId        = _mapGroupId,
                ActiveContextId   = ActiveContextId,
                JsonSchemaVersion = IosLogicConstants.JsonSchemaVersion,
                ConfigurationJson = patch
            });
            _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicConfig,
                $"AREA_AUTHORING ctx={ActiveContextId:N}");
        }

        FdpLog<IosLogic>.Debug(
            "[TRACE-IOS] Area Authoring Mode ON. ContextId={0}", ActiveContextId);
    }

    /// <inheritdoc/>
    public void StartEditingMode(long networkEntityId)
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;

        if (_commandWriter != null)
        {
            string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                contextId      = ActiveContextId.ToString("N"),
                entityId       = networkEntityId
            });
            _commandWriter.Write(new MapCommandRequest
            {
                RequestId       = Guid.NewGuid(),
                MapId           = _targetMapId,
                Type            = CommandType.CMD_START_EDITING,
                CommandArgsJson = argsJson,
            });
            _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicCommand,
                $"CMD_START_EDITING entityId={networkEntityId} ctx={ActiveContextId:N}");
        }

        FdpLog<IosLogic>.Debug(
            "[TRACE-IOS] Editing Mode ON. ContextId={0} EntityId={1}", ActiveContextId, networkEntityId);
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

    // ── IMapPickService ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<GeoPosition> PickLocationAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_commandWriter == null)
            return Task.FromException<GeoPosition>(
                new InvalidOperationException("No MapCommandRequest writer available."));

        CancelPendingPick();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;
        PickMode        = IosPickMode.Location;

        var tcs = new TaskCompletionSource<GeoPosition>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLocationTcs = tcs;

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                if (_pendingLocationTcs == tcs)
                {
                    _pendingLocationTcs = null;
                    PickMode = IosPickMode.None;
                }
                tcs.TrySetCanceled(ct);
            }, useSynchronizationContext: false);
        }

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId = ActiveContextId.ToString("N")
        });

        _commandWriter.Write(new MapCommandRequest
        {
            RequestId       = Guid.NewGuid(),
            MapId           = _targetMapId,
            Type            = CommandType.CMD_PICK_LOCATION,
            CommandArgsJson = argsJson,
        });

        _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicCommand,
            $"CMD_PICK_LOCATION ctx={ActiveContextId:N}");

        FdpLog<IosLogic>.Debug("[TRACE-IOS] PickLocation Mode ON. ContextId={0}", ActiveContextId);

        return tcs.Task;
    }

    /// <inheritdoc/>
    public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_commandWriter == null)
            return Task.FromException<int>(
                new InvalidOperationException("No MapCommandRequest writer available."));

        CancelPendingPick();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;
        PickMode        = IosPickMode.Entity;

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingEntityTcs = tcs;

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                if (_pendingEntityTcs == tcs)
                {
                    _pendingEntityTcs = null;
                    PickMode = IosPickMode.None;
                }
                tcs.TrySetCanceled(ct);
            }, useSynchronizationContext: false);
        }

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId = ActiveContextId.ToString("N"),
            filters   = filterPresets ?? Array.Empty<string>()
        });

        _commandWriter.Write(new MapCommandRequest
        {
            RequestId       = Guid.NewGuid(),
            MapId           = _targetMapId,
            Type            = CommandType.CMD_PICK_ENTITY,
            CommandArgsJson = argsJson,
        });

        _interactionPanel.AddLog("TX", IosLogicConstants.LogTopicCommand,
            $"CMD_PICK_ENTITY filters=[{string.Join(",", filterPresets ?? Array.Empty<string>())}] ctx={ActiveContextId:N}");

        FdpLog<IosLogic>.Debug("[TRACE-IOS] PickEntity Mode ON. ContextId={0}", ActiveContextId);

        return tcs.Task;
    }

    /// <summary>
    /// Cancels any pending location or entity pick without completing it.
    /// Called automatically when a new pick or placement mode starts.
    /// </summary>
    public void CancelPendingPick()
    {
        var locTcs = _pendingLocationTcs;
        _pendingLocationTcs = null;
        locTcs?.TrySetCanceled();

        var entTcs = _pendingEntityTcs;
        _pendingEntityTcs = null;
        entTcs?.TrySetCanceled();

        if (PickMode == IosPickMode.Location || PickMode == IosPickMode.Entity)
            PickMode = IosPickMode.None;
    }



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

            switch (PickMode)
            {
                case IosPickMode.EntityCreation:
                    ProcessEntityCreationClick(evt);
                    break;

                case IosPickMode.Location:
                    ProcessLocationPickClick(evt);
                    break;

                case IosPickMode.Entity:
                    ProcessEntityPickClick(evt);
                    break;

                default:
                    _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicClick,
                        "DROP – no active pick mode");
                    break;
            }
        }
    }

    private void ProcessEntityCreationClick(MapClickEvent evt)
    {
        if (PlacementType == 0)
        {
            _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicClick,
                "DROP – no placement type configured");
            return;
        }

        var requestId = Guid.NewGuid();
        TransactionManager.TrackRequest(requestId, $"Create entity tkbType={PlacementType}");

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

    private void ProcessLocationPickClick(MapClickEvent evt)
    {
        var tcs = _pendingLocationTcs;
        _pendingLocationTcs = null;
        PickMode            = IosPickMode.None;

        _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicClick,
            $"LOCATION_PICK pos={evt.Position.Latitude:F4},{evt.Position.Longitude:F4}");

        tcs?.TrySetResult(evt.Position);
    }

    private void ProcessEntityPickClick(MapClickEvent evt)
    {
        int entityId = evt.HitStack is { Count: > 0 } ? evt.HitStack[0].EntityId : 0;

        var tcs = _pendingEntityTcs;
        _pendingEntityTcs = null;
        PickMode          = IosPickMode.None;

        _interactionPanel.AddLog("RX", IosLogicConstants.LogTopicClick,
            $"ENTITY_PICK entityId={entityId}");

        tcs?.TrySetResult(entityId);
    }

    private void ProcessSelectionEvents()
    {
        while (_selectionQueue.TryDequeue(out var evt))
        {
            if (_mapGroupId != 0 && evt.MapId != 0 && evt.MapId != _mapGroupId)
                continue;

            ContextMenuLogic.OnSelectionChanged(evt);
            SelectedEntityId = evt.SelectedEntityIds is { Count: > 0 }
                ? evt.SelectedEntityIds[0]
                : PanelConstants.InspectorNoSelection;
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

    /// <summary>
    /// Builds the JSON config patch that activates area authoring.
    /// </summary>
    private static string BuildAreaAuthoringPatch(string styleOverrideJson = "")
    {
        if (string.IsNullOrEmpty(styleOverrideJson))
        {
            return JsonConvert.SerializeObject(new
            {
                interaction = new
                {
                    activeTool = IosLogicConstants.AreaAuthoringToolName
                }
            });
        }

        return JsonConvert.SerializeObject(new
        {
            interaction = new
            {
                activeTool   = IosLogicConstants.AreaAuthoringToolName,
                toolSettings = new
                {
                    styleOverrideJson
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
