using Fdp.Core;

namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// Adapts an external selection-changed notification source (e.g. DDS SelectionChangedEvent)
/// to the EditorSelectionStore. The bridge updates SelectedEntity on the store when
/// an external selection arrives.
/// </summary>
public interface IGSelectionBridge : IDisposable
{
    bool IsConnected { get; }
    void Connect(EditorSelectionStore store);
    void Disconnect();
}
