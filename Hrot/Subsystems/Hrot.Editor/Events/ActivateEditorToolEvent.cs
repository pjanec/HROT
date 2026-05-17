namespace Hrot.Editor.Events;

/// <summary>
/// Published on <see cref="Fdp.Core.FdpEventBus"/> when the user selects a new
/// interactive editor tool from the toolbar.
/// </summary>
public sealed class ActivateEditorToolEvent
{
    public EditorTool Tool { get; init; }

    // Required by FdpAutoSerializer for managed event deserialization
    public ActivateEditorToolEvent() { }

    public ActivateEditorToolEvent(EditorTool tool)
    {
        Tool = tool;
    }
}
