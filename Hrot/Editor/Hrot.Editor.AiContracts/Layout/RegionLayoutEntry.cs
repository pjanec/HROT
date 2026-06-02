using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Canvas position and display properties for a single HSM parallel region.
/// </summary>
public sealed class RegionLayoutEntry
{
    public Vector2 Position { get; init; }
    public Vector2? SizeOverride { get; init; }
    public string? Comment { get; init; }
    public bool Collapsed { get; init; }
    public string? Color { get; init; }
    /// <summary>
    /// Structural index (zero-based position among sibling regions) used for stable
    /// VisualId lookup after region deletion or reordering.
    /// </summary>
    public int RegionIndex { get; init; }
}
