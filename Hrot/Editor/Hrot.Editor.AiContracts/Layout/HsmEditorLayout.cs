using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Snapshot of canvas-level layout data for an HSM asset.
/// Returned by an <see cref="HsmLayoutAttribute"/>-decorated method; consumed by the editor on open.
/// </summary>
public sealed class HsmEditorLayout
{
    public Vector2 PanOffset { get; init; }
    public float ZoomLevel { get; init; }
    public IReadOnlyDictionary<Guid, StateLayoutEntry> States { get; init; } =
        new Dictionary<Guid, StateLayoutEntry>();
    public IReadOnlyDictionary<Guid, TransitionLayoutEntry> Transitions { get; init; } =
        new Dictionary<Guid, TransitionLayoutEntry>();
    public IReadOnlyDictionary<Guid, RegionLayoutEntry> Regions { get; init; } =
        new Dictionary<Guid, RegionLayoutEntry>();
    public IReadOnlyList<(string VariableName, string WriterPairKey)> BlackboardConflictSuppressions { get; init; } =
        Array.Empty<(string, string)>();
    public IReadOnlyList<string> UnusedWarningSuppressions { get; init; } =
        Array.Empty<string>();

    /// <summary>⭐ <c>W7b</c> (§9.4) — variables whose cross-region concurrent writes the designer
    /// explicitly allowed. ⛔ PER VARIABLE, unlike the per-(variable, writer-pair) conflict
    /// suppressions above.</summary>
    public IReadOnlyList<string> ConcurrentWritesAllowed { get; init; } =
        Array.Empty<string>();
}
