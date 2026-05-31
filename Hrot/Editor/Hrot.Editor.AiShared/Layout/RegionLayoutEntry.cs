using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

public sealed class RegionLayoutEntry
{
    public Vector2 Position { get; init; }
    public Vector2? SizeOverride { get; init; }
    public string? Comment { get; init; }
    public bool Collapsed { get; init; }
    public string? Color { get; init; }
    // Structural index (zero-based position among sibling regions) used for stable
    // VisualId lookup after region deletion or reordering.
    public int RegionIndex { get; init; }
}
