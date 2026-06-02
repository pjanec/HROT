using System.Numerics;

namespace Hrot.Editor.AiShared.Layout;

/// <summary>
/// Canvas waypoints and display properties for a single HSM transition arc.
/// </summary>
public sealed class TransitionLayoutEntry
{
    public Vector2[] Waypoints { get; init; } = Array.Empty<Vector2>();
    public string? Comment { get; init; }
    public string? Color { get; init; }
}
