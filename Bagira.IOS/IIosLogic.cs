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
    /// Brings the spawner panel to the foreground / opens the spawner flow.
    /// Typically called when the user clicks "New Unit…" in the ORBAT panel.
    /// </summary>
    void OpenSpawner();
}
