using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Command requesting that the UI selects the specified entity.
/// </summary>
[DataPolicy(DataPolicy.NoReplay)]   // ⭐ the coordinator's replay-UI-state handoff ② (CE-4049) — a UI command: never recorded, never re-fired by a player
[EventId(8103)]
public struct SelectEntityCommand
{
    /// <summary>Network entity ID to select.</summary>
    public long NetworkId;
}
