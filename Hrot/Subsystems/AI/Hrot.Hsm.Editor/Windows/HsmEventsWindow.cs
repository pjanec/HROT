using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

// Window that displays the event declarations of the loaded HSM asset.
public sealed class HsmEventsWindow
{
    public const string WindowId = "hsm_events";

    private readonly HsmAsset _asset;

    public HsmEventsWindow(HsmAsset asset)
    {
        _asset = asset;
    }

    public void Render()
    {
        // TODO: render ImGui window showing events from _asset.Events
        // Columns: ID, Name, Payload, Flags, Priority, Global
    }
}
