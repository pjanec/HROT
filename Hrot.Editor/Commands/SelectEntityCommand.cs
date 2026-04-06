namespace Hrot.Editor.Commands;

/// <summary>
/// Managed command requesting that the editor selects the specified entity.
/// </summary>
public sealed class SelectEntityCommand
{
    /// <summary>Network entity ID to select.</summary>
    public long NetworkId { get; init; }
}
