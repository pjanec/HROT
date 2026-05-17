using Fdp.Core;

namespace Hrot.Editor.Commands;

/// <summary>
/// Command requesting the map canvas to pan and zoom to the specified entity.
/// </summary>
[EventId(8104)]
public struct CenterOnEntityCommand
{
    /// <summary>Network entity ID to centre the view on.</summary>
    public long NetworkId;
}
