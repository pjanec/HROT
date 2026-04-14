namespace Hrot.Editor.Commands;

/// <summary>
/// Managed command requesting that the editor opens the rename dialog for the specified entity.
/// </summary>
public sealed class OpenRenameDialogCommand
{
    /// <summary>Network entity ID whose rename dialog should be opened.</summary>
    public long NetworkId { get; init; }
}
