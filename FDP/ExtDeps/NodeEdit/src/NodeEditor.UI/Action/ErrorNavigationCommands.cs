using System.Numerics;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Action;

/// <summary>
/// BP-20 — jump to the next / previous problem node.
///
/// <para>
/// <see cref="CommandCatalog.NextError"/> and <see cref="CommandCatalog.PrevError"/> were declared
/// and never registered, and there was no error-list UI. The data was already there:
/// <see cref="NodeState.Error"/> and <see cref="NodeState.Warning"/> are set by the host's node
/// model (a Blueprint marks an unresolved CLR call or a stale component bake), and the canvas
/// already paints them. What was missing was any way to *find* one on a graph too large to scan.
/// </para>
///
/// <para>
/// Errors are visited before warnings, so a broken graph walks the things that stop it compiling
/// first. Within each severity the order is the model's own, which is stable across invocations.
/// </para>
/// </summary>
public static class ErrorNavigationCommands
{
    /// <summary>Registers <c>editor.next-error</c> and <c>editor.prev-error</c>.</summary>
    public static void Register(EditorCommandsImpl cmds, GraphView view)
    {
        var reg = new CommandRegistration(cmds);

        reg.Add(
            CommandCatalog.NextError, "Next Issue", "Navigate",
            _ => Step(view, forward: true),
            isEnabled: () => Problems(view).Count > 0,
            description: "Selects and centres the next node with an error or warning.",
            defaultKey: new KeyBinding(EditorKey.F8, KeyModifiers.None));

        reg.Add(
            CommandCatalog.PrevError, "Previous Issue", "Navigate",
            _ => Step(view, forward: false),
            isEnabled: () => Problems(view).Count > 0,
            description: "Selects and centres the previous node with an error or warning.",
            defaultKey: new KeyBinding(EditorKey.F8, KeyModifiers.Shift));
    }

    /// <summary>
    /// Problem nodes, errors first. Ordering by severity rather than position means the first F8
    /// on a broken graph lands on something that actually stops the build.
    /// </summary>
    internal static IReadOnlyList<INodeModel> Problems(GraphView view)
        => view.Model.Nodes
            .Where(n => n.State is NodeState.Error or NodeState.Warning)
            .OrderBy(n => n.State == NodeState.Error ? 0 : 1)
            .ToList();

    /// <summary>
    /// Moves to the next problem relative to the current selection, wrapping at both ends.
    ///
    /// <para>
    /// Anchoring on the selection rather than on a stored cursor means the sequence stays correct
    /// after the user clicks elsewhere, and after a fix removes a node from the list — a stored
    /// index would silently skip one.
    /// </para>
    /// </summary>
    private static void Step(GraphView view, bool forward)
    {
        var problems = Problems(view);
        if (problems.Count == 0) return;

        int current = -1;
        var selected = view.Selection.Nodes.ToHashSet();
        for (int i = 0; i < problems.Count; i++)
            if (selected.Contains(problems[i].Id)) { current = i; break; }

        int next = current < 0
            ? (forward ? 0 : problems.Count - 1)
            : (current + (forward ? 1 : -1) + problems.Count) % problems.Count;

        Reveal(view, problems[next]);
    }

    /// <summary>Selects the node and brings it to the centre of the canvas.</summary>
    private static void Reveal(GraphView view, INodeModel node)
    {
        view.Selection.ReplaceWith(SelectionEntry.OfNode(node.Id));

        var size = node.SizeOverride ?? new Vector2(160f, 64f);
        view.Viewport.FrameRect(new RectF(node.Position, size));
    }
}
