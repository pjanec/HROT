using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Canvas position and display properties for a single BTree node.
/// </summary>
public sealed class NodeLayoutEntry
{
    public Vector2 Position { get; init; }
    public Vector2? SizeOverride { get; init; }
    public string? Comment { get; init; }
    public bool Collapsed { get; init; }
    public string? Color { get; init; }
    public string? ExpressionTarget { get; init; }
}
