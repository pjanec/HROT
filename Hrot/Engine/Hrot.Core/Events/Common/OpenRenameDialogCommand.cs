using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Command requesting that the UI opens the rename dialog for the specified entity.
/// </summary>
[DataPolicy(DataPolicy.NoReplay)]   // ⭐ the coordinator's replay-UI-state handoff ② (CE-4049) — a UI command: never recorded, never re-fired by a player
[EventId(8102)]
public struct OpenRenameDialogCommand
{
    /// <summary>Network entity ID whose rename dialog should be opened.</summary>
    public long NetworkId;
}
