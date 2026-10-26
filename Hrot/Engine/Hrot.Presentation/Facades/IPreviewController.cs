namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for controlling the scenario preview mode.
/// When in preview mode, entity authoring operations are suspended and
/// the map renders the simulated state of the scenario.
/// </summary>
public interface IPreviewController
{
    /// <summary>Gets a value indicating whether the application is currently in preview mode.</summary>
    bool IsInPreviewMode { get; }

    /// <summary>Enters preview mode, pausing authoring interactions and starting scenario simulation.</summary>
    void EnterPreviewMode(bool startPaused = false);

    /// <summary>Exits preview mode, restoring authoring interactions.</summary>
    void ExitPreviewMode();

    /// <summary>⭐ <c>CE-1025</c> (R-1031) — leaves preview WITHOUT rewinding: the restore point is forgotten and the world kept as it is
    /// (a scenario load is about to replace it). A no-op when not in preview.</summary>
    void ForgetPreview();
}
