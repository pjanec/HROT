using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.IG.Abstractions;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Tools;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Patching;
using FDP.Toolkit.Vis2D;

namespace Hrot.IG.Systems;

/// <summary>
/// Orchestrates IG-side tool activation from <see cref="Hrot.NED.Messages.MapCommandRequest"/>
/// messages and bridges the results back to the ExCon via <see cref="MapCommandAck"/> messages.
///
/// <para>
/// This control layer decouples IG map tools from any specific network protocol.
/// Tools (e.g. <see cref="CreationTool"/>) receive C# delegates and are unaware of
/// DDS. The controller:
/// <list type="bullet">
///   <item>Creates the appropriate tool with injected delegates.</item>
///   <item>Forwards <see cref="CreateEntityRequest"/> to SimHost when the tool delegate fires.</item>
///   <item>Correlates incoming <see cref="CreateEntityAck"/> samples back to the originating
///         session and publishes a <see cref="MapCommandAck"/> to the ExCon.</item>
///   <item>Detects tool cancellation (via the <see cref="CreationTool.Exited"/> event) and
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

    private readonly MapCanvas                _canvas;
    private readonly FdpEventBus              _eventBus;
    private readonly IDdsWriter<MapCommandAck> _ackWriter;

    // ── Active-session state ──────────────────────────────────────────────────

    /// <summary>The original <c>MapCommandRequest.RequestId</c> of the active session.</summary>
    private Guid _sessionRequestId;

    /// <summary>Context ID provided by the ExCon for the active session.</summary>
    private Guid _sessionContextId;

    /// <summary>Tool pushed onto the canvas for this session.</summary>
    private CreationTool? _activeCreationTool;

    /// <summary>Whether the active tool has already exited the stack (either via success-pop or
    /// cancellation). Used to decide whether to send a final vs intermediate ack when a
    /// late <see cref="CreateEntityAck"/> arrives after the tool has already gone.
    /// </summary>
    private bool _toolFinished;

    /// <summary>
    /// Per-session name-generator delegate created by <see cref="IgApplication"/> when
    /// auto-naming is requested (<see cref="Hrot.NED.Messages.EntityPropertyPatch.AutogenerateName"/>).
    /// Invoked once per left-click inside <see cref="CreationTool"/> to produce a unique
    /// sequential entity name (e.g. "Tank-3", "Tank-4", …). <c>null</c> when no
    /// auto-naming is active for the current session.
    /// </summary>
    private Func<string>? _nameGenerator;

    /// <summary>
    /// Pending entity-creation request IDs forwarded to SimHost but not yet acknowledged.
    /// Maps <c>CreateEntityRequest.RequestId → true</c>. When all entries are resolved and
    /// the tool is finished the session is closed.
    /// </summary>
    private readonly Dictionary<Guid, bool> _pendingEntityRequests = new();

    /// <summary>
    /// Side-channel storage for pre-built <see cref="CreateEntityRequest"/> objects from the
    /// area/route authoring pipeline. The <see cref="SpawnEntityCommandEgressTranslator"/> 
    /// retrieves these via <see cref="TryDequeuePrebuilt"/> to write the full request to DDS
    /// without losing any NED descriptors (dtMapVisualOverlay, dtMapRoute, etc.).
    /// </summary>
    private readonly Dictionary<Guid, CreateEntityRequest> _prebuiltRequests = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="canvas">The <see cref="MapCanvas"/> on which tools are pushed and popped.</param>
    /// <param name="createEntityWriter">
    /// Writer used to forward <see cref="CreateEntityRequest"/> messages to the SimHost when
    /// the tool delegate fires.
    /// </param>
    /// <param name="ackWriter">
    /// Writer used to publish <see cref="MapCommandAck"/> messages back to the ExCon.
    /// </param>
    /// <param name="edgeCompiler">
    /// Optional <see cref="JsonToRecordCompiler"/> forwarded to <see cref="CreationTool"/>
    /// <param name="canvas">The <see cref="MapCanvas"/> on which tools are pushed and popped.</param>
    /// <param name="eventBus">
    /// The FDP event bus used to publish <see cref="SpawnEntityCommand"/> events when the tool fires.
    /// </param>
    /// <param name="ackWriter">
    /// Writer used to publish <see cref="MapCommandAck"/> messages back to the ExCon.
    /// </param>
    public MapCommandController(
        MapCanvas                 canvas,
        FdpEventBus               eventBus,
        IDdsWriter<MapCommandAck> ackWriter)
    {
        _canvas   = canvas   ?? throw new ArgumentNullException(nameof(canvas));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _ackWriter = ackWriter ?? throw new ArgumentNullException(nameof(ackWriter));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates a <see cref="CreationTool"/> session for a <c>CMD_PLACE_ENTITY</c> command.
    ///
    /// <para>Guarded against duplicate activations: if a session with the same context ID is
    /// already active, the call is a no-op.</para>
    /// </summary>
    /// <param name="requestId">The original <c>MapCommandRequest.RequestId</c>; echoed in all acks.</param>
    /// <param name="contextId">The ExCon-provided interaction context ID.</param>
    /// <param name="tkbType">TKB template type for the entity to create.</param>
    /// <param name="geoTransform">
    /// Optional geographic transform; passed through to <see cref="CreationTool"/>.
    /// </param>
    /// <param name="initialPropertiesJson">
    /// Optional JSON override blob forwarded to <see cref="CreationTool"/>. The tool
    /// recognises <c>name</c> (string), <c>affiliation</c> (string) and ignores unknown fields.
    /// </param>
    /// <param name="nameGenerator">
    /// Optional per-click name-generator delegate. When provided it is passed to
    /// <see cref="CreationTool"/> as its <c>nameResolver</c>, overriding any <c>name</c>
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

        // Pop any leftover CreationTool from a previous session.
        if (_canvas.ActiveTool is CreationTool)
            _canvas.PopTool();

        ClearSession();

        _sessionRequestId = requestId;
        _sessionContextId = contextId;
        _toolFinished     = false;
        _nameGenerator    = nameGenerator;

        var tool = new CreationTool(
            onEntityCreated:       OnEntityCreatedByTool,
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson,
            autoPopOnPlace:        true,
            nameResolver:          _nameGenerator);   // null when no auto-naming

        tool.Exited += OnCreationToolExited;
        _activeCreationTool = tool;
        _canvas.PushTool(tool);

        FdpLog<MapCommandController>.Info(
            "[MapCommandController] PlacementTool activated. RequestId={0} ContextId={1} TKB={2}",
            requestId, contextId, tkbType);
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
            "[MapCommandController] AreaAuthoring session started. RequestId={0} ContextId={1}",
            requestId, contextId);
    }

    /// <summary>
    /// Called by <see cref="IgApplication"/> when the area-authoring tool commits a shape.
    /// Stores the pre-built request in the side-channel dictionary so the egress translator can
    /// retrieve it via <see cref="TryDequeuePrebuilt"/>, then publishes a <see cref="SpawnEntityCommand"/>
    /// on the bus to signal the creation intent. <c>InitialComponents</c> is intentionally left null
    /// to avoid conflicting with <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>'s
    /// ECS component registration path.
    /// </summary>
    public void OnAreaEntityCreated(CreateEntityRequest request, bool isToolDone = true)
    {
        if (_sessionRequestId == Guid.Empty)
        {
            FdpLog<MapCommandController>.Warn(
                "[MapCommandController] OnAreaEntityCreated called with no active session — request dropped.");
            return;
        }

        // Store the fully-built request in the side-channel so the egress translator can
        // retrieve it and write it verbatim to DDS (preserving dtMapVisualOverlay etc.).
        _prebuiltRequests[request.RequestId] = request;

        // Publish the intent command. InitialComponents is NOT set here — that would cause
        // NetworkSpawningSystem to attempt ECS registration of NED struct types, which violates
        // the component type constraint.
        var cmd = new SpawnEntityCommand
        {
            NetworkId             = 0,
            TkbType               = ExtractTkbType(request),
            OwnerNodeId           = 0,
            InitType              = ModuleHost.Core.Network.Interfaces.ReliableInitType.AllPeers,
            RequestId             = request.RequestId,
            InitialAttributesJson = request.InitialAttributesJson,
        };

        _eventBus.PublishManaged(cmd);
        _pendingEntityRequests[request.RequestId] = true;

        if (isToolDone)
            _toolFinished = true;

        TryCloseSessionIfComplete();
    }

    /// <summary>
    /// Retrieves and removes a pre-built <see cref="CreateEntityRequest"/> that was registered
    /// by <see cref="OnAreaEntityCreated"/>. Called by the egress translator to obtain the full
    /// NED descriptor payload when writing to DDS.
    /// </summary>
    /// <param name="requestId">The <see cref="CreateEntityRequest.RequestId"/> to look up.</param>
    /// <returns>The pre-built request if found; otherwise <c>null</c>.</returns>
    internal CreateEntityRequest? TryDequeuePrebuilt(Guid requestId)
    {
        if (_prebuiltRequests.TryGetValue(requestId, out var req))
        {
            _prebuiltRequests.Remove(requestId);
            return req;
        }
        return null;
    }

    private static long ExtractTkbType(CreateEntityRequest request)
    {
        if (request.InitialDescriptors == null) return 0;
        foreach (var desc in request.InitialDescriptors)
            if (desc._d == EDescriptorType.dtEntityMaster)
                return desc.EntityMaster.TkbType;
        return 0;
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
    public void OnCreateEntityAck(CreateUpdateDeleteEntityAck ack)
    {
        if (_sessionRequestId == Guid.Empty) return;
        if (!_pendingEntityRequests.ContainsKey(ack.RequestId)) return;

        // Only remove from pending on terminal ACKs (success or error).
        // InProgress ACK (StatusCode=1) carries the EntityId — use it for routing but keep pending.
        if (ack.StatusCode != (int)NedStatusCode.InProgress)
            _pendingEntityRequests.Remove(ack.RequestId);

        if (ack.StatusCode >= 2)
        {
            FdpLog<MapCommandController>.Warn(
                "[MapCommandController] CreateUpdateDeleteEntityAck error={0} for req={1}",
                ack.StatusCode, ack.RequestId);
            // Treat as intermediate failure; don't abort the session.
            return;
        }

        FdpLog<MapCommandController>.Debug(
            "[MapCommandController] CreateUpdateDeleteEntityAck status={0} entityId={1}", ack.StatusCode, ack.EntityId);

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
    /// Delegate injected into <see cref="CreationTool"/>; called when the operator
    /// left-clicks on the canvas and the tool builds a <see cref="SpawnEntityCommand"/>.
    /// Publishes the command on the event bus so the ACL egress translator
    /// converts it to a DDS <see cref="CreateEntityRequest"/> for SimHost.
    /// </summary>
    private void OnEntityCreatedByTool(SpawnEntityCommand cmd)
    {
        _eventBus.PublishManaged(cmd);
        _pendingEntityRequests[cmd.RequestId] = true;

        FdpLog<MapCommandController>.Debug(
            "[MapCommandController] Published SpawnEntityCommand req={0}", cmd.RequestId);
    }

    /// <summary>
    /// Called (via the <see cref="CreationTool.Exited"/> event) when the active
    /// <see cref="CreationTool"/> pops off the canvas.
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
        _ackWriter.Write(new MapCommandAck
        {
            RequestId  = _sessionRequestId,
            StatusCode = statusCode,
            DataJson   = dataJson,
        });

        FdpLog<MapCommandController>.Info(
            "[MapCommandController] MapCommandAck published. RequestId={0} Status={1} Data={2}",
            _sessionRequestId, statusCode, dataJson);
    }

    private void ClearSession()
    {
        _sessionRequestId   = Guid.Empty;
        _sessionContextId   = Guid.Empty;
        _toolFinished       = false;
        _activeCreationTool = null;
        _nameGenerator      = null;
        _pendingEntityRequests.Clear();
    }

    private static string BuildEntityIdJson(int entityId)
        => $"{{\"entityId\":{entityId}}}";
}
