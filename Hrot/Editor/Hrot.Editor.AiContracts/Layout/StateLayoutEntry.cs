using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Canvas position and display properties for a single HSM state node.
/// </summary>
public sealed class StateLayoutEntry
{
    public Vector2 Position { get; init; }
    public Vector2? SizeOverride { get; init; }
    public string? Comment { get; init; }
    public bool Collapsed { get; init; }
    public string? Color { get; init; }
}
