using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Services;
using FDP.Toolkit.DER;

namespace Bagira.IOS;

/// <summary>
/// Facade interface consumed by all IOS UI panels.
///
/// <para>Decouples the panel classes from the concrete application-shell
/// (<c>IosLogic</c>) so panels can be unit-tested with a lightweight stub or
/// Moq mock without needing a live DDS participant or a Raylib window.</para>
/// </summary>
public interface IIosLogic
{
    // ── State read-access ─────────────────────────────────────────────────────

    /// <summary>DER entity repository – panels read simulation state from here.</summary>
    IDerRepo Repo { get; }

    /// <summary>Service for reading and committing entity mission plans.</summary>
    IMissionEditorService MissionEditorService { get; }

    /// <summary>
    /// Service for async map-side location and entity picks triggered by the
    /// operator clicking the IG canvas.
    /// </summary>
    IMapPickService MapPickService { get; }

    /// <summary>In-flight DDS request tracker – exposes the pending queue for diagnostics.</summary>
    IRequestTransactionManager TransactionManager { get; }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises panel config state into a JSON Merge Patch (RFC 7396) and
    /// publishes it as a <see cref="Bagira.BDC.SSTD.MapInteractionConfig"/> message.
    /// </summary>
    void SendConfigPatch(string jsonPatch);

    /// <summary>
    /// Activates the map placement tool with the specified TKB entity type.
    /// The application shell generates a new context ID and publishes the
    /// appropriate <c>MapInteractionConfig</c> or
    /// <c>MapCommandRequest(CMD_PLACE_ENTITY)</c>.
    /// </summary>
    /// <param name="initialPropertiesJson">
    /// Optional JSON object with initial property overrides forwarded to the IG
    /// (e.g. <c>{"name":"Alpha-1","affiliation":"FORCE_FRIENDLY"}</c>).
    /// Affiliation and other entity properties are embedded here rather than
    /// passed as dedicated parameters. Ignored when using the legacy
    /// <c>MapInteractionConfig</c> fallback path.
    /// </param>
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null);

    /// <summary>
    /// Typed overload for <see cref="StartPlacementMode(long, string?)"/>.
    /// Serialises <paramref name="initialProperties"/> to JSON (null properties omitted)
    /// and delegates to the string-based overload.
    /// </summary>
    /// <param name="initialProperties">
    /// Strongly-typed property patch.  Only non-<c>null</c> fields are serialised,
    /// so the IG ignores unspecified fields and does not override TKB defaults for them.
    /// Pass <c>null</c> to use no initial-property overrides.
    /// </param>
    void StartPlacementMode(long tkbType, EntityPropertyPatch? initialProperties);

    /// <summary>
    /// Activates polygonal area authoring. The application shell generates a
    /// new context ID and publishes the appropriate <c>MapInteractionConfig</c>.
    /// </summary>
    /// <param name="styleOverrideJson">
    /// Optional JSON fragment describing the desired rendering style for the
    /// overlay (fill colour, border colour, line thickness).  Pass an empty
    /// string or omit to use the IG default style.
    /// </param>
    void StartAreaAuthoringMode(string styleOverrideJson = "");

    /// <summary>
    /// Activates the polyline route authoring tool. The operator draws a shared
    /// <c>TacGraphic_Route</c> entity by clicking waypoints on the map canvas.
    /// </summary>
    void StartRouteAuthoringMode();

    /// <summary>
    /// Activates the area polygon editing tool for the specified entity,
    /// allowing operators to drag individual vertices of an existing overlay.
    /// A <see cref="MapCommandRequest"/> with <c>CMD_START_EDITING</c> is
    /// sent to the IG.
    /// </summary>
    /// <param name="networkEntityId">
    /// The DDS / network entity ID of the area overlay entity to edit.
    /// </param>
    void StartEditingMode(long networkEntityId);

    /// <summary>
    /// Selects a single entity, updating any entity-dependent panels (e.g.
    /// the Mission Panel) and forwarding the selection to the IG.
    /// </summary>
    void SelectEntity(int entityId);

    /// <summary>
    /// Applies a local selection optimistically and publishes
    /// <c>MapCommandRequest(CMD_SET_SELECTION, {"entityId": id})</c> to the IG.
    /// </summary>
    void SendSetSelection(int entityId);

    /// <summary>
    /// Publishes <c>MapCommandRequest(CMD_SET_VIEW, {"entityId": id})</c>
    /// to request that the IG centres its camera on the specified entity.
    /// </summary>
    void CenterOnEntity(int entityId);

    /// <summary>
    /// Publishes a <see cref="Bagira.BDC.SSTM.DeleteEntityRequest"/> for the given
    /// entity ID, adds it to the pending-delete set, and waits for the
    /// <see cref="Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck"/> to confirm or fail.
    /// </summary>
    void DeleteEntity(int entityId);

    /// <summary>
    /// Returns <c>true</c> while a <see cref="DeleteEntity"/> call has been issued
    /// for the given entity but the ACK has not yet arrived.
    /// Used to disable the ORBAT row while deletion is in flight.
    /// </summary>
    bool IsEntityPendingDelete(int entityId);

    /// <summary>
    /// Generates a new <see cref="ActiveContextId"/> and publishes
    /// <c>MapCommandRequest(CMD_DRAW_PERSONAL_ROUTE, {"contextId":"…","entityId":vehicleEntityId})</c>
    /// to the IG, which will activate the route-authoring tool for the specified vehicle.
    /// </summary>
    void StartPersonalRouteAuthoring(int vehicleEntityId);

    /// <summary>
    /// Returns <c>true</c> while a Phase-1 InProgress ACK has been received for
    /// the given entity but the Phase-2 final ACK has not yet arrived.
    /// Used to guard mission and context-menu interactions against half-baked entities.
    /// </summary>
    bool IsEntityPending(int entityId);

    /// <summary>
    /// A non-<c>null</c> value indicates that a Phase-2 failure ACK was received
    /// and the error message should be surfaced to the operator as a modal alert.
    /// Call <see cref="DismissAlert"/> to clear.
    /// </summary>
    string? GlobalAlert { get; }

    /// <summary>Dismisses the current <see cref="GlobalAlert"/>, clearing it to <c>null</c>.</summary>
    void DismissAlert();

    /// <summary>
    /// Brings the spawner panel to the foreground / opens the spawner flow.
    /// Typically called when the user clicks "New Unit…" in the ORBAT panel.
    /// </summary>
    void OpenSpawner();

    // ── Time state (observed from network) ───────────────────────────────────

    /// <summary>Current simulation time in seconds, received via TimePulseDescriptor.</summary>
    double MasterSimTime   { get; }

    /// <summary>Current wall-clock ticks (UTC), received via TimePulseDescriptor.</summary>
    long   MasterWallTicks { get; }

    /// <summary>Current time scale factor, received via TimePulseDescriptor.</summary>
    float  MasterTimeScale { get; }

    /// <summary>True when the simulation is paused (TimeMode = Deterministic).</summary>
    bool   IsPaused        { get; }

    // ── Time commands (dispatched to Orchestrator over DDS) ──────────────────

    /// <summary>Sends a PauseTime SysOpRequest to the Orchestrator.</summary>
    void RequestPause();

    /// <summary>Sends a ResumeTime SysOpRequest to the Orchestrator.</summary>
    void RequestResume();

    /// <summary>Sends a StepTime SysOpRequest to the Orchestrator.</summary>
    void RequestStep();

    /// <summary>Sends a SetTimeScale SysOpRequest with the given scale to the Orchestrator.</summary>
    void SetTimeScale(float scale);
}
