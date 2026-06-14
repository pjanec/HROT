using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;

namespace NodeEditor.UI.Action;

/// <summary>
/// Shared helper that routes a palette entry pick to the correct command:
/// either <c>AddAttachment</c> (for decorator/AttachToSelected entries) or
/// <c>AddNode</c> (for normal CreateNode entries).
///
/// Centralising this logic ensures the Tab/Space picker (CanvasInput) and the
/// canvas right-click "Add Node…" picker (CanvasRenderer) behave identically,
/// and that decorator entries can never accidentally become free-standing nodes.
/// </summary>
public static class PaletteEntryExecutor
{
    /// <summary>
    /// Execute a palette pick. Resolves the correct command and dispatches it through
    /// <paramref name="view"/>'s command pipeline.
    /// </summary>
    /// <param name="view">The graph view that owns the command pipeline and selection state.</param>
    /// <param name="entry">The catalog entry that was picked.</param>
    /// <param name="graphPos">Canvas-space position for new nodes (ignored for AttachToSelected).</param>
    public static void Execute(GraphView view, NodeCatalogEntry entry, Vector2 graphPos)
    {
        var cb = new CommandBuilder(view.Model);

        if (entry.PaletteAction == NodePaletteAction.AttachToSelected)
        {
            var hosts = view.Selection.Nodes.ToList();
            if (hosts.Count == 1)
            {
                var host = hosts[0];
                int stackIndex = view.Model.GetAttachmentsForNode(host).Count;
                var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = entry.Kind.Id };
                var (fwd, inv) = cb.AddAttachment(host, entry.AttachmentCategory ?? AttachmentCategory.Custom,
                    glyph: null, label: entry.DisplayName, tooltip: entry.Description, stackIndex, props);
                view.Execute(fwd, inv, "Add Decorator");
            }
            // else: zero or >1 selected → safe no-op (decorator requires exactly one host)
        }
        else
        {
            var (fwd, inv) = cb.AddNode(entry.Kind, graphPos, null);
            view.Execute(fwd, inv, "Add Node");
        }
    }
}
