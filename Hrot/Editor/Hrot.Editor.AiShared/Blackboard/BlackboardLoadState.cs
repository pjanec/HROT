namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Describes the load-time health of an editor-managed blackboard companion file.
/// Used by <see cref="IBlackboardManagedAsset"/> to gate save operations and drive
/// state-aware banners in <see cref="Hrot.Editor.AiShared.Windows.BlackboardAuthoringWindow"/>.
/// </summary>
public enum BlackboardLoadState
{
    /// <summary>The companion file was parsed and loaded without issues.</summary>
    Clean,

    /// <summary>
    /// The companion struct was located but at least one field declaration did not fit the
    /// constrained single-line form that allows verbatim-span capture.
    /// The window renders in read-only mode and offers a "Save anyway (lossy)" option.
    /// </summary>
    SpanCaptureFailed,

    /// <summary>
    /// The companion file was found but the target struct could not be located (e.g., the
    /// file structure has deviated from the expected form).
    /// The window renders in read-only mode; saving is blocked.
    /// </summary>
    StructParseFailed,

    /// <summary>
    /// The companion assembly failed to compile or load; the blackboard type is unavailable.
    /// The window replaces its client area with an error banner; saving is blocked.
    /// </summary>
    AssemblyFailed,
}
