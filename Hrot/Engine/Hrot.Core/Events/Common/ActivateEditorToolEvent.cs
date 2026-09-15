using Fdp.Core;
using Hrot.Common;

namespace Hrot.Common.Events;

/// <summary>
/// Published on <see cref="Fdp.Core.FdpEventBus"/> when the user selects a new
/// interactive editor tool from the toolbar.
/// </summary>
[EventId(8105)]
public struct ActivateEditorToolEvent
{
    public EditorTool Tool;

    public ActivateEditorToolEvent(EditorTool tool)
    {
        Tool = tool;
    }
}
