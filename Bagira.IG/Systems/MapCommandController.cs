using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Abstractions;
using Bagira.IG.Components;
using Bagira.IG.Tools;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Patching;
using FDP.Toolkit.Vis2D;

namespace Bagira.IG.Systems;

/// <summary>
/// Orchestrates IG-side tool activation from <see cref="Bagira.BDC.SSTM.MapCommandRequest"/>
/// messages and bridges the results back to the IOS via <see cref="MapCommandAck"/> messages.
///
/// <para>
/// This control layer decouples IG map tools from any specific network protocol.
/// Tools (e.g. <see cref="CreationTool"/>) receive C# delegates and are unaware of
/// DDS. The controller:
/// <list type="bullet">
///   <item>Creates the appropriate tool with injected delegates.</item>
///   <item>Forwards <see cref="CreateEntityRequest"/> to SimHost when the tool delegate fires.</item>
///   <item>Correlates incoming <see cref="CreateEntityAck"/> samples back to the originating
///         session and publishes a <see cref="MapCommandAck"/> to the IOS.</item>
///   <item>Detects tool cancellation (via the <see cref="CreationTool.Exited"/> event) and
///         publishes a cancellation <see cref="MapCommandAck"/> so the IOS can close its
///         pending interaction session.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Status-code semantics of <see cref="MapCommandAck.StatusCode"/>:</b>
/// <list type="bullet">
///   <item><c>0</c> – Session finished (IOS may close the request).</item>
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

    private readonly MapCanvas                         _canvas;
    private readonly IDdsWriter<CreateEntityRequest>   _createEntityWriter;
    private readonly IDdsWriter<MapCommandAck>         _ackWriter;

    // ── Active-session state ──────────────────────────────────────────────────

    /// <summary>The original <c>MapCommandRequest.RequestId</c> of the active session.</summary>
    private Guid _sessionRequestId;

    /// <summary>Context ID provided by the IOS for the active session.</summary>
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
    /// auto-naming is requested (<see cref="Bagira.BDC.SSTM.EntityPropertyPatch.AutogenerateName"/>).
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
    /// Edge compiler injected at construction time and forwarded to every
    /// <see cref="CreationTool"/> so placement requests carry binary
    /// <see cref="AttributeRecord"/>s instead of raw JSON on the DDS wire.
    /// <c>null</c> in headless / test constructors that do not supply one.
    /// </summary>
    private readonly JsonToRecordCompiler? _edgeCompiler;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="canvas">The <see cref="MapCanvas"/> on which tools are pushed and popped.</param>
    /// <param name="createEntityWriter">
    /// Writer used to forward <see cref="CreateEntityRequest"/> messages to the SimHost when
    /// the tool delegate fires.
    /// </param>
    /// <param name="ackWriter">
    /// Writer used to publish <see cref="MapCommandAck"/> messages back to the IOS.
    /// </param>
    /// <param name="edgeCompiler">
    /// Optional <see cref="JsonToRecordCompiler"/> forwarded to <see cref="CreationTool"/>
    /// for binary attribute encoding.  When non-null the tool emits
    /// <c>InitialAttributeRecords</c> and clears <c>InitialAttributesJson</c>.
    /// When <c>null</c> (default / tests) the tool falls back to the legacy JSON wire.
    /// </param>
    public MapCommandController(
        MapCanvas                        canvas,
        IDdsWriter<CreateEntityRequest>  createEntityWriter,
        IDdsWriter<MapCommandAck>        ackWriter,
        JsonToRecordCompiler?            edgeCompiler = null)
    {
        _canvas              = canvas              ?? throw new ArgumentNullException(nameof(canvas));
        _createEntityWriter  = createEntityWriter  ?? throw new ArgumentNullException(nameof(createEntityWriter));
        _ackWriter           = ackWriter           ?? throw new ArgumentNullException(nameof(ackWriter));
        _edgeCompiler        = edgeCompiler;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates a <see cref="CreationTool"/> session for a <c>CMD_PLACE_ENTITY</c> command.
    ///
    /// <para>Guarded against duplicate activations: if a session with the same context ID is
    /// already active, the call is a no-op.</para>
    /// </summary>
    /// <param name="requestId">The original <c>MapCommandRequest.RequestId</c>; echoed in all acks.</param>
    /// <param name="contextId">The IOS-provided interaction context ID.</param>
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
            geoTransform:          geoTransform,
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson,
            autoPopOnPlace:        true,
            nameResolver:          _nameGenerator,   // null when no auto-naming
            edgeCompiler:          _edgeCompiler);   // ATTR2-DEBT-07: binary encoding for production DDS wire

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
    /// Called by <see cref="IgApplication"/> when the area-authoring tool commits a shape
    /// and wants to send a <see cref="CreateEntityRequest"/> through the controller so the
    /// controller can correlate the <see cref="CreateEntityAck"/> and publish the correct
    /// <see cref="MapCommandAck"/>.
    /// </summary>
    public void OnAreaEntityCreated(CreateEntityRequest request, bool isToolDone = true)
    {
        if (_sessionRequestId == Guid.Empty) return;

        _createEntityWriter.Write(request);
        _pendingEntityRequests[request.RequestId] = true;

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
    /// <see cref="CreateEntityAck"/> to the active session, if any.
    /// </summary>
    public void OnCreateEntityAck(CreateEntityAck ack)
    {
        if (_sessionRequestId == Guid.Empty) return;
        if (!_pendingEntityRequests.ContainsKey(ack.RequestId)) return;

        _pendingEntityRequests.Remove(ack.RequestId);

        if (ack.ErrorCode != 0)
        {
            FdpLog<MapCommandController>.Warn(
                "[MapCommandController] CreateEntityAck error={0} for req={1}",
                ack.ErrorCode, ack.RequestId);
            // Treat as intermediate failure; don't abort the session.
            return;
        }

        FdpLog<MapCommandController>.Debug(
            "[MapCommandController] CreateEntityAck OK entityId={0}", ack.NewEntityId);

        // If the tool is still active → intermediate ack (more entities may follow).
        // If the tool is already done → final ack.
        bool isFinal = _toolFinished && _pendingEntityRequests.Count == 0;
        string dataJson = BuildEntityIdJson(ack.NewEntityId);

        PublishAck(isFinal ? StatusFinished : StatusIntermediate, dataJson);

        if (isFinal)
            ClearSession();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Delegate injected into <see cref="CreationTool"/>; called when the operator
    /// left-clicks on the canvas and the tool builds a <see cref="CreateEntityRequest"/>.
    /// </summary>
    private void OnEntityCreatedByTool(CreateEntityRequest request)
    {
        _createEntityWriter.Write(request);
        _pendingEntityRequests[request.RequestId] = true;

        FdpLog<MapCommandController>.Debug(
            "[MapCommandController] Forwarded CreateEntityRequest req={0}", request.RequestId);
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
