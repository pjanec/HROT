using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Command requesting that the UI opens the rename dialog for the specified entity.
/// </summary>
[EventId(8102)]
public struct OpenRenameDialogCommand
{
    /// <summary>Network entity ID whose rename dialog should be opened.</summary>
    public long NetworkId;
}
