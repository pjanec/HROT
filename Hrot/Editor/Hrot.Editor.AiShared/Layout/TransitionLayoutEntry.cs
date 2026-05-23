using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

public sealed class TransitionLayoutEntry
{
    public Vector2[] Waypoints { get; init; } = Array.Empty<Vector2>();
    public string? Comment { get; init; }
    public string? Color { get; init; }
}
