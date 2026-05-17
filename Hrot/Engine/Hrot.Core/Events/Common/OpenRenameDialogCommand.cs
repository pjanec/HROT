namespace Hrot.Common.Events;

/// <summary>
/// Managed command requesting that the UI opens the rename dialog for the specified entity.
/// </summary>
public sealed class OpenRenameDialogCommand
{
    /// <summary>Network entity ID whose rename dialog should be opened.</summary>
    public long NetworkId { get; init; }
}

