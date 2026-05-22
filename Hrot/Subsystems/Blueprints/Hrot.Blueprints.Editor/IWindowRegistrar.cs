namespace Hrot.Blueprints.Editor;

public interface IWindowRegistrar
{
    void RegisterMenuEntry(string path, Action onSelected);
    void RegisterToolbarEntry(string label, Action onClicked);
    void RegisterShortcut(string keybind, Action onTriggered);
}
