using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Command requesting that the UI selects the specified entity.
/// </summary>
[EventId(8103)]
public struct SelectEntityCommand
{
    /// <summary>Network entity ID to select.</summary>
    public long NetworkId;
}
