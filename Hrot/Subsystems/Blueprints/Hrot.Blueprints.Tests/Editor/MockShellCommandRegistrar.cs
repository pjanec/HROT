using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class MockShellCommandRegistrar : IShellCommandRegistrar
{
    public List<(string Path, Action OnSelected)> MenuEntries { get; } = new();
    public List<(string Label, Action OnClicked)> ToolbarEntries { get; } = new();
    public List<(string Keybind, Action OnTriggered)> Shortcuts { get; } = new();

    public void RegisterMenuEntry(string path, Action onSelected)
        => MenuEntries.Add((path, onSelected));

    public void RegisterToolbarEntry(string label, Action onClicked)
        => ToolbarEntries.Add((label, onClicked));

    public void RegisterShortcut(string keybind, Action onTriggered)
        => Shortcuts.Add((keybind, onTriggered));
}
