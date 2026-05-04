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
}
