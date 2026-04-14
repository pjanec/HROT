using Hrot.Core.Mission;
using Hrot.Core.Network;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Panels;
using Hrot.ExCon.Services;
using Hrot.Map.Common;
using FDP.Kernel.Logging;
using FDP.Toolkit.DER;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost.Core.Time;
using Newtonsoft.Json;

namespace Hrot.ExCon;

/// <summary>
/// Core application-state and network-traffic-cop for the ExCon Mock.
///
/// <para>Implements <see cref="IExConLogic"/> so that all UI panels depend only
/// on the interface, keeping them testable in isolation.</para>
///
/// <para><b>Threading model:</b>
/// <list type="bullet">
///   <item><see cref="Update"/> must be called from the main (Raylib) thread
///   exactly once per frame.  It polls all registered ingress handlers,
///   drains the interaction-log queue (ExCon-DEBT-034), processes buffered DDS
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
public enum ExConPickMode { None, EntityCreation, Location, Entity }

public sealed class ExConLogic : IExConLogic, IMapPickService, Hrot.UI.Common.Facades.ISpawnController, IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IDerRepo Repo { get; }

    /// <inheritdoc/>
    public IMissionEditorService MissionEditorService { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="ExConLogic"/> implements <see cref="IMapPickService"/> directly.
    /// </remarks>
    public IMapPickService MapPickService => this;

    /// <summary>Context-menu strategy manager; driven by selection-change events.</summary>
    public IContextMenuLogic ContextMenuLogic { get; }

    /// <summary>In-flight request tracker for all outgoing DDS requests.</summary>
    public IRequestTransactionManager TransactionManager { get; }

    private readonly long                               _localNodeId;
    private readonly IExConEgressWriters                _egressWriters;
    private readonly IEventQueue<MapClickEventDto>      _clickQueue;
    private readonly IEventQueue<SelectionChangedEventDto> _selectionQueue;
    private readonly IEventQueue<EntityLifecycleAckDto> _createEntityAckQueue;
    private readonly IEventQueue<MapCommandAckDto>?     _mapCommandAckQueue;
    private readonly InteractionPanel                   _interactionPanel;
    private readonly List<IIngressHandler>              _ingressHandlers;
    private readonly int                                _mapGroupId;
    /// <summary>MapId of the IG instance that receives tool-activation commands (0 = broadcast).</summary>
    private readonly int                                _targetMapId;
    private readonly ITimeControlGateway?               _timeControl;

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
    public ExConPickMode PickMode { get; private set; } = ExConPickMode.None;

    private TaskCompletionSource<GeoPoint>? _pendingLocationTcs;
    private TaskCompletionSource<int>?         _pendingEntityTcs;

    private bool _disposed;

    /// <summary>
    /// Set of entity IDs for which a Phase-1 InProgress ACK has been received
    /// but the Phase-2 final ACK has not yet arrived.
    /// </summary>
    private readonly HashSet<int> _pendingEntities = new();

    /// <summary>
    /// Set of entity IDs for which a <see cref="DeleteEntity"/> call has been issued
    /// but the <see cref="Hrot.NED.Messages.CreateUpdateDeleteEntityAck"/> confirmation
    /// has not yet arrived.
    /// </summary>
    private readonly HashSet<int> _pendingDeleteEntityIds = new();

    /// <summary>Holds the last Phase-2 failure message, or null if none.</summary>
    private string? _globalAlert;

    /// <summary>
    /// The <c>MapCommandRequest.RequestId</c> of the most recently sent command (CMD_PLACE_ENTITY
    /// or CMD_START_AUTHORING). Used to correlate incoming <see cref="MapCommandAck"/> samples.
    /// <see cref="Guid.Empty"/> when no command is outstanding.
    /// </summary>
    private Guid _lastCommandRequestId;

    // ── Time state ────────────────────────────────────────────────────────────
    public double MasterSimTime   { get; private set; }
    public long   MasterWallTicks { get; private set; }
    public float  MasterTimeScale { get; private set; } = 1f;
    public bool   IsPaused        { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="ExConLogic"/> with all dependencies injected.
    /// </summary>
    public ExConLogic(
        IDerRepo                              repo,
        IMissionEditorService                 missionEditorService,
        IContextMenuLogic                     contextMenuLogic,
        IRequestTransactionManager            transactionManager,
        IExConEgressWriters                   egressWriters,
        IEventQueue<MapClickEventDto>         clickQueue,
        IEventQueue<SelectionChangedEventDto> selectionQueue,
        InteractionPanel                      interactionPanel,
        IEventQueue<EntityLifecycleAckDto>    createEntityAckQueue,
        IEnumerable<IIngressHandler>?         ingressHandlers    = null,
        int                                   mapGroupId         = ExConLogicConstants.DefaultMapGroupId,
        int                                   targetMapId        = ExConLogicConstants.DefaultTargetMapId,
        IEventQueue<MapCommandAckDto>?        mapCommandAckQueue = null,
        ITimeControlGateway?                  timeControl        = null,
        long                                  localNodeId        = 0)
    {
        _localNodeId          = localNodeId;
        Repo                  = repo                 ?? throw new ArgumentNullException(nameof(repo));
        MissionEditorService  = missionEditorService ?? throw new ArgumentNullException(nameof(missionEditorService));
        ContextMenuLogic      = contextMenuLogic     ?? throw new ArgumentNullException(nameof(contextMenuLogic));
        TransactionManager    = transactionManager   ?? throw new ArgumentNullException(nameof(transactionManager));
        _egressWriters        = egressWriters        ?? throw new ArgumentNullException(nameof(egressWriters));
        _clickQueue           = clickQueue           ?? throw new ArgumentNullException(nameof(clickQueue));
        _selectionQueue       = selectionQueue       ?? throw new ArgumentNullException(nameof(selectionQueue));
        _createEntityAckQueue = createEntityAckQueue ?? throw new ArgumentNullException(nameof(createEntityAckQueue));
        _mapCommandAckQueue   = mapCommandAckQueue;   // null-ok
        _interactionPanel     = interactionPanel     ?? throw new ArgumentNullException(nameof(interactionPanel));
        _ingressHandlers      = ingressHandlers?.ToList() ?? new List<IIngressHandler>();
        _mapGroupId           = mapGroupId;
        _targetMapId          = targetMapId;
        _timeControl          = timeControl;

        Repo.EntityDeleted += OnEntityDeleted;
    }

    // ── IExConLogic ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void SendConfigPatch(string jsonPatch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(jsonPatch);

        _egressWriters.WriteMapConfig(new MapConfigDto
        {
            ActiveContextId = ActiveContextId,
            ConfigJson      = jsonPatch,
        });

        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicConfig,
            $"patch={jsonPatch.Length}ch");
    }

    /// <inheritdoc/>
    public void StartPlacementMode(long tkbType, EntityPropertyPatch? initialProperties)
    {
        string? propsJson = initialProperties == null
            ? null
            : JsonConvert.SerializeObject(
                initialProperties,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        StartPlacementMode(tkbType, propsJson);
    }

    /// <inheritdoc/>
    public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = tkbType;
        PickMode        = ExConPickMode.EntityCreation;
        CancelPendingPick();

        var requestId = Guid.NewGuid();
        _lastCommandRequestId = requestId;
        TransactionManager.TrackRequest(requestId, $"CMD_PLACE_ENTITY tkb={tkbType}");

        var argsObj = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["contextId"]  = ActiveContextId.ToString("N"),
            ["entityType"] = tkbType,
        };
        if (!string.IsNullOrEmpty(initialPropertiesJson))
            argsObj["initialPropertiesJson"] = initialPropertiesJson;
        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(argsObj);

        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = requestId,
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_PLACE_ENTITY",
            CommandArgsJson = argsJson,
        });
        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCommand,
            $"CMD_PLACE_ENTITY tkb={tkbType} ctx={ActiveContextId:N}");

        FdpLog<ExConLogic>.Debug(
            "[Node-{0}] Placement Mode ON. ContextId={1} TKB={2}", _localNodeId, ActiveContextId, tkbType);
    }

    /// <inheritdoc/>
    public void StartAreaAuthoringMode(string styleOverrideJson = "")
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;

        var requestId = Guid.NewGuid();
        _lastCommandRequestId = requestId;
        TransactionManager.TrackRequest(requestId, $"CMD_START_AUTHORING ctx={ActiveContextId:N}");

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId        = ActiveContextId.ToString("N"),
            styleOverrideJson
        });
        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = requestId,
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_START_AUTHORING",
            CommandArgsJson = argsJson,
        });
        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCommand,
            $"CMD_START_AUTHORING ctx={ActiveContextId:N}");

        FdpLog<ExConLogic>.Debug(
            "[Node-{0}] Area Authoring Mode ON. ContextId={1}", _localNodeId, ActiveContextId);
    }

    /// <inheritdoc/>
    public void StartRouteAuthoringMode()
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;

        var requestId = Guid.NewGuid();
        _lastCommandRequestId = requestId;
        TransactionManager.TrackRequest(requestId, $"CMD_START_AUTHORING (Route) ctx={ActiveContextId:N}");

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId = ActiveContextId.ToString("N"),
            tkbType   = TkbEntityTypes.TacGraphic_Route
        });

        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = requestId,
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_START_AUTHORING",
            CommandArgsJson = argsJson,
        });
        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCommand,
            $"CMD_START_AUTHORING (Route) ctx={ActiveContextId:N}");

        FdpLog<ExConLogic>.Debug(
            "[Node-{0}] Route Authoring Mode ON. ContextId={1}", _localNodeId, ActiveContextId);
    }

    /// <inheritdoc/>
    public void StartEditingMode(long networkEntityId)
    {
        ThrowIfDisposed();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId = ActiveContextId.ToString("N"),
            entityId  = networkEntityId
        });
        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = Guid.NewGuid(),
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_START_EDITING",
            CommandArgsJson = argsJson,
        });
        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCommand,
            $"CMD_START_EDITING entityId={networkEntityId} ctx={ActiveContextId:N}");

        FdpLog<ExConLogic>.Debug(
            "[Node-{0}] Editing Mode ON. ContextId={1} EntityId={2}", _localNodeId, ActiveContextId, networkEntityId);
    }

    /// <inheritdoc/>
    public void SelectEntity(int entityId)
    {
        ThrowIfDisposed();
        SelectedEntityId = entityId;
    }

    /// <inheritdoc/>
    public void SendSetSelection(int entityId)
    {
        ThrowIfDisposed();
        SelectEntity(entityId);
        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = Guid.NewGuid(),
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_SET_SELECTION",
            CommandArgsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { entityId }),
        });
    }

    /// <inheritdoc/>
    public void CenterOnEntity(int entityId)
    {
        ThrowIfDisposed();
        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = Guid.NewGuid(),
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_SET_VIEW",
            CommandArgsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { entityId }),
        });
    }

    /// <inheritdoc/>
    public void DeleteEntity(int entityId)
    {
        ThrowIfDisposed();
        _pendingDeleteEntityIds.Add(entityId);
        _egressWriters.WriteDeleteEntity(entityId);
    }

    /// <inheritdoc/>
    public bool IsEntityPendingDelete(int entityId) => _pendingDeleteEntityIds.Contains(entityId);

    /// <inheritdoc/>
    public void StartPersonalRouteAuthoring(int vehicleEntityId)
    {
        ThrowIfDisposed();
        ActiveContextId = Guid.NewGuid();
        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = Guid.NewGuid(),
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_DRAW_PERSONAL_ROUTE",
            CommandArgsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                contextId = ActiveContextId.ToString("N"),
                entityId  = vehicleEntityId,
            }),
        });
    }

    /// <inheritdoc/>
    public bool IsEntityPending(int entityId) => _pendingEntities.Contains(entityId);

    /// <inheritdoc/>
    public string? GlobalAlert => _globalAlert;

    /// <inheritdoc/>
    public void DismissAlert() => _globalAlert = null;

    /// <inheritdoc/>
    public void OpenSpawner()
    {
        ThrowIfDisposed();
        SpawnerRequested = true;
    }

    /// <summary>
    /// Resets the spawner-requested flag after the UI shell has acted on it.
    /// Call from <see cref="ExConMock.Update"/> after forwarding the flag to the
    /// spawner panel.
    /// </summary>
    public void ConsumeSpawnerRequest() => SpawnerRequested = false;

    // ── IMapPickService ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        CancelPendingPick();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;
        PickMode        = ExConPickMode.Location;

        var tcs = new TaskCompletionSource<GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLocationTcs = tcs;

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                if (_pendingLocationTcs == tcs)
                {
                    _pendingLocationTcs = null;
                    PickMode = ExConPickMode.None;
                }
                tcs.TrySetCanceled(ct);
            }, useSynchronizationContext: false);
        }

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId = ActiveContextId.ToString("N")
        });

        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = Guid.NewGuid(),
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_PICK_LOCATION",
            CommandArgsJson = argsJson,
        });

        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCommand,
            $"CMD_PICK_LOCATION ctx={ActiveContextId:N}");

        FdpLog<ExConLogic>.Debug("[Node-{0}] PickLocation Mode ON. ContextId={1}", _localNodeId, ActiveContextId);

        return tcs.Task;
    }

    /// <inheritdoc/>
    public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_egressWriters == null)
            return Task.FromException<int>(
                new InvalidOperationException("No egress writers available."));

        CancelPendingPick();

        ActiveContextId = Guid.NewGuid();
        PlacementType   = 0;
        PickMode        = ExConPickMode.Entity;

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingEntityTcs = tcs;

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                if (_pendingEntityTcs == tcs)
                {
                    _pendingEntityTcs = null;
                    PickMode = ExConPickMode.None;
                }
                tcs.TrySetCanceled(ct);
            }, useSynchronizationContext: false);
        }

        string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            contextId = ActiveContextId.ToString("N"),
            filters   = filterPresets ?? Array.Empty<string>()
        });

        _egressWriters.WriteMapCommand(new MapCommandDto
        {
            RequestId       = Guid.NewGuid(),
            TargetMapId     = _targetMapId,
            CommandType     = "CMD_PICK_ENTITY",
            CommandArgsJson = argsJson,
        });

        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCommand,
            $"CMD_PICK_ENTITY filters=[{string.Join(",", filterPresets ?? Array.Empty<string>())}] ctx={ActiveContextId:N}");

        FdpLog<ExConLogic>.Debug("[Node-{0}] PickEntity Mode ON. ContextId={1}", _localNodeId, ActiveContextId);

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

        if (PickMode == ExConPickMode.Location || PickMode == ExConPickMode.Entity)
            PickMode = ExConPickMode.None;
    }



    /// <summary>
    /// Called once per frame from the application shell (main thread).
    ///
    /// <para>Execution order:
    /// <list type="number">
    ///   <item>Poll all registered DDS ingress handlers → feeds the DER repo.</item>
    ///   <item>Drain the interaction-log staging queue (ExCon-DEBT-034).</item>
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

        // 2. Drain pending log entries onto the main thread (ExCon-DEBT-034)
        _interactionPanel.DrainPendingLogs();

        // 3. Process event queues
        ProcessClickEvents();
        ProcessSelectionEvents();
        ProcessEntityCreationAcks();
        ProcessMapCommandAcks();

        // 4. Check request timeouts
        TransactionManager.CheckTimeouts();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ProcessClickEvents()
    {
        while (_clickQueue.TryDequeue(out var evt))
        {
            FdpLog<ExConLogic>.Debug(
                "[Node-{0}] MapClickEvent ContextId={1} (expected {2})",
                _localNodeId,
                evt.InteractionContextId,
                ActiveContextId);

            // Drop stale clicks: context ID must match the one we published.
            if (evt.InteractionContextId != ActiveContextId)
            {
                _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicClick,
                    $"DROP ctx={evt.InteractionContextId:N} (expected {ActiveContextId:N})");
                continue;
            }

            switch (PickMode)
            {
                case ExConPickMode.EntityCreation:
                    ProcessEntityCreationClick(evt);
                    break;

                case ExConPickMode.Location:
                    ProcessLocationPickClick(evt);
                    break;

                case ExConPickMode.Entity:
                    ProcessEntityPickClick(evt);
                    break;

                default:
                    _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicClick,
                        "DROP - no active pick mode");
                    break;
            }
        }
    }

    private void ProcessEntityCreationClick(MapClickEventDto evt)
    {
        if (PlacementType == 0)
        {
            _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicClick,
                "DROP - no placement type configured");
            return;
        }

        var requestId = Guid.NewGuid();
        TransactionManager.TrackRequest(requestId, $"Create entity tkbType={PlacementType}");

        _egressWriters.WriteCreateEntity(new CreateEntityCommand
        {
            RequestId = requestId,
            TkbType   = PlacementType,
            Latitude  = evt.Latitude,
            Longitude = evt.Longitude,
            Altitude  = evt.Altitude,
        });

        _interactionPanel.AddLog("TX", ExConLogicConstants.LogTopicCreate,
            $"tkb={PlacementType} pos={evt.Latitude:F2},{evt.Longitude:F2}");
    }

    private void ProcessLocationPickClick(MapClickEventDto evt)
    {
        var tcs = _pendingLocationTcs;
        _pendingLocationTcs = null;
        PickMode            = ExConPickMode.None;

        _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicClick,
            $"LOCATION_PICK pos={evt.Latitude:F4},{evt.Longitude:F4}");

        tcs?.TrySetResult(new GeoPoint(evt.Latitude, evt.Longitude, evt.Altitude));
    }

    private void ProcessEntityPickClick(MapClickEventDto evt)
    {
        int entityId = evt.HitEntityIds is { Count: > 0 } ? evt.HitEntityIds[0] : 0;

        var tcs = _pendingEntityTcs;
        _pendingEntityTcs = null;
        PickMode          = ExConPickMode.None;

        _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicClick,
            $"ENTITY_PICK entityId={entityId}");

        tcs?.TrySetResult(entityId);
    }

    private void ProcessEntityCreationAcks()
    {
        while (_createEntityAckQueue.TryDequeue(out var ack))
        {
            // ── Delete ACK path ────────────────────────────────────────────────
            if (_pendingDeleteEntityIds.Contains(ack.EntityId))
            {
                _pendingDeleteEntityIds.Remove(ack.EntityId);
                if (ack.StatusCode >= EntityLifecycleAckDto.StatusFailureMin)
                    _globalAlert = $"Entity deletion failed (code {ack.StatusCode}).";
                _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicCreateAck,
                    $"DELETE-ACK entityId={ack.EntityId} status={ack.StatusCode}");
                continue;
            }

            if (ack.StatusCode == EntityLifecycleAckDto.StatusInProgress)
            {
                // Phase 1: ID is now known; guard the entity against interactions
                // until the ELM handshake completes.
                _pendingEntities.Add(ack.EntityId);

                _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicCreateAck,
                    $"INPROGRESS newId={ack.EntityId} req={ack.RequestId:N}");
                FdpLog<ExConLogic>.Debug("[Node-{0}] CreateAck InProgress: newId={1}", _localNodeId, ack.EntityId);
                continue;
            }

            if (ack.StatusCode >= EntityLifecycleAckDto.StatusFailureMin)
            {
                // Phase 2 failure: remove from pending, surface alert, fail transaction.
                _pendingEntities.Remove(ack.EntityId);
                _globalAlert = $"Entity creation failed (code {ack.StatusCode}).";

                TransactionManager.CompleteRequest(ack.RequestId, success: false,
                    $"StatusCode={ack.StatusCode}");

                _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicCreateAck,
                    $"FAIL req={ack.RequestId:N} status={ack.StatusCode}");
                FdpLog<ExConLogic>.Warn("[Node-{0}] CreateAck FAILED: req={1} status={2}",
                    _localNodeId, ack.RequestId, ack.StatusCode);
                continue;
            }

            // Phase 2 success (StatusCode == 0).
            _pendingEntities.Remove(ack.EntityId);
            TransactionManager.CompleteRequest(ack.RequestId, success: true, null);

            _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicCreateAck,
                $"OK newId={ack.EntityId} req={ack.RequestId:N}");
            FdpLog<ExConLogic>.Debug("[Node-{0}] CreateAck OK: newId={1}", _localNodeId, ack.EntityId);
            SelectEntity(ack.EntityId);
        }
    }

    private void ProcessMapCommandAcks()
    {
        if (_mapCommandAckQueue == null) return;

        while (_mapCommandAckQueue.TryDequeue(out var ack))
        {
            bool isOurRequest = ack.RequestId == _lastCommandRequestId
                                && _lastCommandRequestId != Guid.Empty;

            if (!isOurRequest)
            {
                FdpLog<ExConLogic>.Debug(
                    "[Node-{0}] MapCommandAck ignored (unknown req={1})", _localNodeId, ack.RequestId);
                continue;
            }

            bool isFinal = ack.StatusCode == 0 || ack.StatusCode == 2; // Finished or Cancelled

            _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicCommand,
                $"MapCommandAck status={ack.StatusCode} data={ack.DataJson} req={ack.RequestId:N}");
            FdpLog<ExConLogic>.Debug(
                "[Node-{0}] MapCommandAck status={1} req={2}", _localNodeId, ack.StatusCode, ack.RequestId);

            if (isFinal)
            {
                bool success = ack.StatusCode == 0;
                TransactionManager.CompleteRequest(
                    ack.RequestId, success,
                    success ? null : "Cancelled by IG");
                _lastCommandRequestId = Guid.Empty;

                if (PickMode == ExConPickMode.EntityCreation)
                    PickMode = ExConPickMode.None;
            }
        }
    }

    private void ProcessSelectionEvents()
    {
        while (_selectionQueue.TryDequeue(out var evt))
        {
            if (_mapGroupId != 0 && evt.MapId != 0 && evt.MapId != _mapGroupId)
                continue;

            ContextMenuLogic.OnSelectionChanged(evt, IsEntityPending);
            SelectedEntityId = evt.SelectedEntityIds is { Count: > 0 }
                ? evt.SelectedEntityIds[0]
                : PanelConstants.InspectorNoSelection;
            _interactionPanel.AddLog("RX", ExConLogicConstants.LogTopicSelection,
                $"{evt.SelectedEntityIds?.Count ?? 0} entities");
        }
    }

    /// <summary>
    /// Parses the raw <c>"affiliation"</c> string value from a JSON property bag.
    /// Returns <c>null</c> when absent or malformed.
    /// </summary>
    private static string? ParseAffiliationStringFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("affiliation", out var el)
             && el.ValueKind == System.Text.Json.JsonValueKind.String)
                return el.GetString();
        }
        catch { /* malformed JSON */ }
        return null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ExConLogic));
    }

    // ── Time state ingress ────────────────────────────────────────────────────

    /// <summary>Called by TimeModeIngressHandler to update IsPaused state.</summary>
    public void OnTimeMode(SwitchTimeModeWireDto dto)
    {
        IsPaused = (TimeMode)dto.TargetModeInt == TimeMode.Deterministic;
    }

    // ── Time commands ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void RequestPause()  => _timeControl?.RequestPause();

    /// <inheritdoc/>
    public void RequestResume() => _timeControl?.RequestResume();

    /// <inheritdoc/>
    public void RequestStep()   => _timeControl?.RequestStep();

    /// <inheritdoc/>
    public void SetTimeScale(float scale) => _timeControl?.SetTimeScale(scale);

    /// <summary>
    /// Clears <see cref="SelectedEntityId"/> when the currently-selected entity is deleted
    /// from the DER repository so the inspector does not display stale data.
    /// Only reacts if the deleted entity matches the currently selected one; no-op otherwise.
    /// </summary>
    private void OnEntityDeleted(IDerEntity entity)
    {
        if (SelectedEntityId == entity.EntityId)
            SelectedEntityId = 0;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>Marks the instance as disposed; idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Repo.EntityDeleted -= OnEntityDeleted;
    }
}
