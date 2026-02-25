using Bagira.BDC.SSTD;
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

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises panel config state into a JSON Merge Patch (RFC 7396) and
    /// publishes it as a <see cref="Bagira.BDC.SSTD.MapInteractionConfig"/> message.
    /// </summary>
    void SendConfigPatch(string jsonPatch);

    /// <summary>
    /// Activates the map placement tool with the specified TKB entity type and
    /// force affiliation. The application shell generates a new context ID and
    /// publishes the appropriate <c>MapInteractionConfig</c>.
    /// </summary>
    void StartPlacementMode(long tkbType, eForceIdentifier affiliation);

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
