using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.ScenarioEditor.Gizmos;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;

namespace Hrot.IG.Systems;

/// <summary>
/// Orchestrates IG-side tool activation from <see cref="Hrot.NED.Messages.MapCommandRequest"/>
/// messages and bridges the results back to the ExCon via <see cref="MapCommandAck"/> messages.
///
/// <para>
/// This control layer decouples IG map tools from any specific network protocol.
/// An <see cref="EntityPlacementGizmo"/> is registered with <see cref="GlobalGizmoManager"/>
/// when a placement session starts; the gizmo receives C# delegates and is unaware of DDS.
/// The controller:
/// <list type="bullet">
///   <item>Creates the appropriate gizmo and registers it with <see cref="GlobalGizmoManager"/>.</item>
///   <item>Forwards <see cref="CreateEntityRequest"/> to SimHost when the tool delegate fires.</item>
///   <item>Correlates incoming <see cref="CreateEntityAck"/> samples back to the originating
///         session and publishes a <see cref="MapCommandAck"/> to the ExCon.</item>
///   <item>Detects tool cancellation (via the <c>onRemove</c> callback) and
///         publishes a cancellation <see cref="MapCommandAck"/> so the ExCon can close its
///         pending interaction session.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Status-code semantics of <see cref="MapCommandAck.StatusCode"/>:</b>
/// <list type="bullet">
///   <item><c>0</c> – Session finished (ExCon may close the request).</item>
///   <item><c>1</c> – Intermediate result: an entity was confirmed, but the tool is still
///         active and may produce more entities.</item>
///   <item><c>2</c> – Cancelled: the tool was dismissed without creating any entity.</item>
/// </list>
/// </para>
/// </summary>
public class MapCommandController
{
    // ── Status codes (StatusCode field of MapCommandAck) ─────────────────────
    public const long StatusFinished    = 0L;
    public const long StatusIntermediate = 1L;
    public const long StatusCancelled   = 2L;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly MapCanvas                   _canvas;
    private readonly FdpEventBus                _eventBus;
    private readonly Action<MapCommandAckDto>   _ackCallback;
    private readonly long                       _localNodeId;
    private readonly GlobalGizmoManager?        _globalGizmoManager;

    // ── Active-session state ──────────────────────────────────────────────────

    /// <summary>The original <c>MapCommandRequest.RequestId</c> of the active session.</summary>
    private Guid _sessionRequestId;

    /// <summary>Context ID provided by the ExCon for the active session.</summary>
    private Guid _sessionContextId;

    /// <summary>Stable id under which the active <see cref="EntityPlacementGizmo"/> is registered
    /// with <see cref="_globalGizmoManager"/>. Zero when no placement session is active.</summary>
    private long _activePlacementId;

    /// <summary>Whether the active tool has already exited the stack (either via success-pop or
    /// cancellation). Used to decide whether to send a final vs intermediate ack when a
    /// late <see cref="CreateEntityAck"/> arrives after the tool has already gone.
    /// </summary>
    private bool _toolFinished;

    /// <summary>
    /// Per-session name-generator delegate created by <see cref="IgApplication"/> when
    /// auto-naming is requested (<see cref="Hrot.NED.Messages.EntityPropertyPatch.AutogenerateName"/>).
    /// Invoked once per left-click inside <see cref="EntityPlacementGizmo"/> to produce a unique
    /// sequential entity name (e.g. "Tank-3", "Tank-4", ...). <c>null</c> when no
    /// auto-naming is active for the current session.
    /// </summary>
    private Func<string>? _nameGenerator;

    /// <summary>
    /// Pending entity-creation request IDs forwarded to SimHost but not yet acknowledged.
    /// Maps <c>CreateEntityRequest.RequestId → true</c>. When all entries are resolved and
    /// the tool is finished the session is closed.
    /// </summary>
    private readonly Dictionary<Guid, bool> _pendingEntityRequests = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="canvas">The <see cref="MapCanvas"/> on which tools may be pushed and popped.</param>
    /// <param name="eventBus">
    /// The FDP event bus used to publish <see cref="SpawnEntityCommand"/> events when the tool fires.
    /// </param>
    /// <param name="ackCallback">
    /// Writer used to publish <see cref="MapCommandAck"/> messages back to the ExCon.
    /// </param>
    /// <param name="globalGizmoManager">
    /// Manager used to register/unregister the <see cref="EntityPlacementGizmo"/> for each
    /// placement session. When <c>null</c> the gizmo is not activated.
    /// </param>
    public MapCommandController(
        MapCanvas                  canvas,
        FdpEventBus                eventBus,
        Action<MapCommandAckDto>   ackCallback,
        long                       localNodeId        = 0,
        GlobalGizmoManager?        globalGizmoManager = null)
    {
        _canvas             = canvas      ?? throw new ArgumentNullException(nameof(canvas));
        _eventBus           = eventBus    ?? throw new ArgumentNullException(nameof(eventBus));
        _ackCallback        = ackCallback ?? throw new ArgumentNullException(nameof(ackCallback));
        _localNodeId        = localNodeId;
        _globalGizmoManager = globalGizmoManager;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates an <see cref="EntityPlacementGizmo"/> session for a <c>CMD_PLACE_ENTITY</c> command.
    ///
    /// <para>Guarded against duplicate activations: if a session with the same context ID is
    /// already active, the call is a no-op.</para>
    /// </summary>
    /// <param name="requestId">The original <c>MapCommandRequest.RequestId</c>; echoed in all acks.</param>
    /// <param name="contextId">The ExCon-provided interaction context ID.</param>
    /// <param name="tkbType">TKB template type for the entity to create.</param>
    /// <param name="geoTransform">
    /// Optional geographic transform; passed through for future use.
    /// </param>
    /// <param name="initialPropertiesJson">
    /// Optional JSON override blob forwarded to <see cref="EntityPlacementGizmo"/>. The gizmo
    /// recognises <c>name</c> (string), <c>affiliation</c> (string) and ignores unknown fields.
    /// </param>
    /// <param name="nameGenerator">
    /// Optional per-click name-generator delegate. When provided it is passed to
    /// <see cref="EntityPlacementGizmo"/> as its <c>nameResolver</c>, overriding any <c>name</c>
    /// encoded in <paramref name="initialPropertiesJson"/>. Typically built by
    /// <see cref="IgApplication"/> via <see cref="UniqueNameGenerator.CreateSessionGenerator"/>
    /// when <c>AutogenerateName</c> is set in the placement patch.
    /// </param>
    public void ActivatePlacementCommand(
        Guid                  requestId,
        Guid                  contextId,
        long                  tkbType,
        IGeographicTransform? geoTransform,
        string?               initialPropertiesJson = null,
        Func<string>?         nameGenerator         = null)
    {
        // Guard: same context already active.
        if (contextId != Guid.Empty && contextId == _sessionContextId && !_toolFinished)
            return;

        // Unregister any leftover placement gizmo from a previous session.
        if (_activePlacementId != 0)
            _globalGizmoManager?.Unregister(_activePlacementId);

        ClearSession();

        _sessionRequestId = requestId;
        _sessionContextId = contextId;
        _toolFinished     = false;
        _nameGenerator    = nameGenerator;

        var id = GlobalGizmoManager.NewId();
        var gizmo = new EntityPlacementGizmo(
            onEntityCreated:       OnEntityCreatedByTool,
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson,
            autoPopOnPlace:        true,
            nameResolver:          _nameGenerator,
            onRemove:              () =>
            {
                _globalGizmoManager?.Unregister(id);
                OnCreationToolExited();
            });
        _activePlacementId = id;
        _globalGizmoManager?.Register(id, gizmo);

        FdpLog<MapCommandController>.Info(
            "[Node-{0}] PlacementTool activated. RequestId={1} ContextId={2} TKB={3}",
            _localNodeId, requestId, contextId, tkbType);
    }

    /// <summary>
    /// Begins a session for a <c>CMD_START_AUTHORING</c> area-authoring command.
    ///
    /// <para>
    /// Unlike <see cref="ActivatePlacementCommand"/> this method does NOT push a tool;
    /// that is handled by the caller (<see cref="IgApplication"/>), which already contains
    /// the area-specific descriptor-building logic. The caller must invoke
    /// <see cref="OnAreaEntityCreated"/> when the tool commits and
    /// <see cref="OnAreaToolCancelled"/> when the tool is dismissed.
    /// </para>
    /// </summary>
    public void BeginAreaAuthoringSession(Guid requestId, Guid contextId)
    {
        ClearSession();
        _sessionRequestId = requestId;
        _sessionContextId = contextId;
        _toolFinished     = false;

        FdpLog<MapCommandController>.Info(
            "[Node-{0}] AreaAuthoring session started. RequestId={1} ContextId={2}",
            _localNodeId, requestId, contextId);
    }

    /// <summary>
    /// Called by the area/route authoring tool callback when the operator commits a shape.
    /// Publishes the <see cref="SpawnEntityCommand"/> (which carries geometry via
    /// <see cref="SpawnEntityCommand.InitialComponents"/>) directly onto the event bus.
    /// The egress translator converts it to a <see cref="CreateEntityRequest"/> for DDS.
    /// </summary>
    public void OnAreaEntityCreated(SpawnEntityCommand cmd, bool isToolDone = true)
    {
        if (_sessionRequestId == Guid.Empty)
        {
            FdpLog<MapCommandController>.Warn(
                "[Node-{0}] OnAreaEntityCreated called with no active session — command dropped.", _localNodeId);
            return;
        }

        _eventBus.PublishManaged(cmd);
        _pendingEntityRequests[cmd.RequestId] = true;

        if (isToolDone)
            _toolFinished = true;

        TryCloseSessionIfComplete();
    }

    /// <summary>
    /// Called by <see cref="IgApplication"/> when the area-authoring tool is cancelled
    /// (the operator pressed ESC or right-clicked before completing the shape).
    /// </summary>
    public void OnAreaToolCancelled()
    {
        if (_sessionRequestId == Guid.Empty) return;

        if (_pendingEntityRequests.Count == 0)
        {
            // Nothing was created — pure cancellation.
            PublishAck(StatusCancelled, string.Empty);
            ClearSession();
        }
        else
        {
            // Some entities were already sent to SimHost; mark tool as finished and wait for acks.
            _toolFinished = true;
        }
    }

    /// <summary>
    /// Called by <see cref="IgApplication"/> each frame to forward an incoming
    /// <see cref="CreateUpdateDeleteEntityAck"/> to the active session, if any.
    /// Only InProgress (StatusCode=1) and Success (StatusCode=0) ACKs with the entity ID are relevant.
    /// </summary>
    public void OnCreateEntityAck(EntityLifecycleAckDto ack)
    {
        if (_sessionRequestId == Guid.Empty) return;
        if (!_pendingEntityRequests.ContainsKey(ack.RequestId)) return;

        // Only remove from pending on terminal ACKs (success or error).
        // InProgress ACK (StatusCode=1) carries the EntityId -- use it for routing but keep pending.
        if (ack.StatusCode != EntityLifecycleAckDto.StatusInProgress)
            _pendingEntityRequests.Remove(ack.RequestId);

        if (ack.StatusCode >= 2)
        {
            FdpLog<MapCommandController>.Warn(
                "[Node-{0}] CreateUpdateDeleteEntityAck error={1} for req={2}",
                _localNodeId, ack.StatusCode, ack.RequestId);
            // Treat as intermediate failure; don't abort the session.
            return;
        }

        FdpLog<MapCommandController>.Debug(
            "[Node-{0}] CreateUpdateDeleteEntityAck status={1} entityId={2}", _localNodeId, ack.StatusCode, ack.EntityId);

        // If the tool is still active → intermediate ack (more entities may follow).
        // If the tool is already done → final ack.
        bool isFinal = _toolFinished && _pendingEntityRequests.Count == 0;
        string dataJson = BuildEntityIdJson(ack.EntityId);

        PublishAck(isFinal ? StatusFinished : StatusIntermediate, dataJson);

        if (isFinal)
            ClearSession();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Delegate injected into <see cref="EntityPlacementGizmo"/>; called when the operator
    /// left-clicks on the canvas and the gizmo builds a <see cref="SpawnEntityCommand"/>.
    /// Publishes the command on the event bus so the ACL egress translator
    /// converts it to a DDS <see cref="CreateEntityRequest"/> for SimHost.
    /// </summary>
    private void OnEntityCreatedByTool(SpawnEntityCommand cmd)
    {
        _eventBus.PublishManaged(cmd);
        _pendingEntityRequests[cmd.RequestId] = true;

        FdpLog<MapCommandController>.Debug(
            "[Node-{0}] Published SpawnEntityCommand req={1}", _localNodeId, cmd.RequestId);
    }

    /// <summary>
    /// Called (via the <c>onRemove</c> callback) when the active
    /// <see cref="EntityPlacementGizmo"/> pops off the canvas.
    /// </summary>
    private void OnCreationToolExited()
    {
        _toolFinished = true;

        if (_pendingEntityRequests.Count == 0)
        {
            // Tool exited without creating any entity → pure cancellation.
            PublishAck(StatusCancelled, string.Empty);
            ClearSession();
        }
        // Otherwise, wait for pending CreateEntityAck(s) before closing.
    }

    private void TryCloseSessionIfComplete()
    {
        if (_toolFinished && _pendingEntityRequests.Count == 0)
        {
            PublishAck(StatusFinished, string.Empty);
            ClearSession();
        }
    }

    private void PublishAck(long statusCode, string dataJson)
    {
        _ackCallback(new MapCommandAckDto
        {
            RequestId  = _sessionRequestId,
            StatusCode = (int)statusCode,
            DataJson   = dataJson,
        });

        FdpLog<MapCommandController>.Info(
            "[Node-{0}] MapCommandAck published. RequestId={1} Status={2} Data={3}",
            _localNodeId, _sessionRequestId, statusCode, dataJson);
    }

    private void ClearSession()
    {
        _sessionRequestId  = Guid.Empty;
        _sessionContextId  = Guid.Empty;
        _toolFinished      = false;
        _activePlacementId = 0;
        _nameGenerator     = null;
        _pendingEntityRequests.Clear();
    }

    private static string BuildEntityIdJson(int entityId)
        => $"{{\"entityId\":{entityId}}}";
}
