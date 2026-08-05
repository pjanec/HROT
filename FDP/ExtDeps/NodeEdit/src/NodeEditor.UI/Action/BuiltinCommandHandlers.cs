using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.View;
using NodeEditor.UI.Find;

namespace NodeEditor.UI.Action;

/// <summary>
/// Registers handlers for all built-in editor commands that the editor itself owns
/// (versus host-owned commands such as Save and Compile).
/// Call once during editor construction after all sub-registrars have run.
/// </summary>
public static class BuiltinCommandHandlers
{
    /// <summary>
    /// Register all built-in commands onto <paramref name="cmds"/>.
    /// </summary>
    /// <param name="cmds">The commands implementation to register onto.</param>
    /// <param name="view">The active graph view (owns undo, selection, model).</param>
    /// <param name="findBar">The find bar instance (may be null if find is not used).</param>
    public static void RegisterAll(
        EditorCommandsImpl cmds,
        GraphView          view,
        FindBar?           findBar)
    {
        EditCommands.Register(cmds, view);
        ViewCommands.Register(cmds, view);
        CanvasCommands.Register(cmds, view, findBar);
        // BP-13: the nine alignment ids CommandCatalog has always declared, with no implementation
        // anywhere until now.
        AlignCommands.Register(cmds, view);
    }
}
