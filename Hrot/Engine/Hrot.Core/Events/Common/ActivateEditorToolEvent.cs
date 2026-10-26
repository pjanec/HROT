using Fdp.Core;
using Hrot.Common;

namespace Hrot.Common.Events;

/// <summary>
/// Published on <see cref="Fdp.Core.FdpEventBus"/> when the user selects a new
/// interactive editor tool from the toolbar.
/// </summary>
[DataPolicy(DataPolicy.NoReplay)]   // ⭐ the coordinator's replay-UI-state handoff ② (CE-4049) — a UI command: never recorded, never re-fired by a player
[EventId(8105)]
public struct ActivateEditorToolEvent
{
    public EditorTool Tool;

    public ActivateEditorToolEvent(EditorTool tool)
    {
        Tool = tool;
    }
}
