using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Find;

namespace NodeEditor.UI.Action;

/// <summary>
/// Registers canvas-specific commands (Find, navigation) on the given
/// <see cref="EditorCommandsImpl"/>.
/// </summary>
public static class CanvasCommands
{
    /// <summary>Register all canvas commands.</summary>
    public static void Register(EditorCommandsImpl cmds, GraphView view, FindBar? findBar)
    {
        var reg = new CommandRegistration(cmds);

        reg.Add(
            CommandCatalog.FindInGraph, "Find in Graph", "Find",
            _ =>
            {
                if (findBar is not null) findBar.Open();
            },
            description: "Open the find bar to search within the current graph.",
            defaultKey: new KeyBinding(EditorKey.F, Primitives.KeyModifiers.Ctrl));

        reg.Add(
            CommandCatalog.FindNext, "Find Next", "Find",
            _ => findBar?.Next(),
            isEnabled: () => findBar?.Results.Count > 0,
            description: "Navigate to the next find result.",
            defaultKey: new KeyBinding(EditorKey.F3, Primitives.KeyModifiers.None));

        reg.Add(
            CommandCatalog.FindPrev, "Find Previous", "Find",
            _ => findBar?.Previous(),
            isEnabled: () => findBar?.Results.Count > 0,
            description: "Navigate to the previous find result.",
            defaultKey: new KeyBinding(EditorKey.F3, Primitives.KeyModifiers.Shift));
    }
}
