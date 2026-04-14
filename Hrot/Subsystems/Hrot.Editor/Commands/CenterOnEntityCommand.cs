namespace Hrot.Editor.Commands;

/// <summary>
/// Managed command requesting the map canvas to pan and zoom to the specified entity.
/// </summary>
public sealed class CenterOnEntityCommand
{
    /// <summary>Network entity ID to centre the view on.</summary>
    public long NetworkId { get; init; }
}
