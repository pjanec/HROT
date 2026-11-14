using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.BTree.Editor.Layout;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// ⭐ <c>CE-1368</c> (BTree host design §3.4) — the empty-canvas right-click item "Reset layout": tidy-tree the WHOLE tree again with the
/// canvas's real font, ignoring the stored positions. It is one <see cref="GraphCommand.MoveNodes"/> through the view's undo stack (Ctrl+Z
/// restores the old positions). The sink decides the rest, exactly as for a drag: a writable JSON asset is marked dirty (a layout change the
/// author asked for); a code-built graph takes it as view state (CE-1275) and is never dirty.
/// </summary>
internal sealed class BTreeCanvasContextMenuProvider : ICanvasContextMenuProvider
{
    private readonly BehaviorTreeAsset _asset;
    private readonly IGraphCommandSink _sink;
    private readonly Func<string, float> _measure;
    private readonly Func<float> _headerHeight;

    /// <summary>Wired from <c>GraphView.Execute</c> so the reset is undoable; null falls back to a non-undoable apply.</summary>
    public Action<GraphCommand, GraphCommand, string>? Recorder { get; set; }

    public BTreeCanvasContextMenuProvider(BehaviorTreeAsset asset, IGraphCommandSink sink, Func<string, float> measure, Func<float> headerHeight)
    {
        _asset = asset; _sink = sink; _measure = measure; _headerHeight = headerHeight;
    }

    public IReadOnlyList<ContextMenuItem> GetItems()
        => new[] { new ContextMenuItem("Reset layout", () => ResetLayout(), true) };

    /// <summary>Re-lays the whole tree; returns false when nothing moved.</summary>
    internal bool ResetLayout()
    {
        var before = _asset.Nodes.ToDictionary(n => n.VisualId, n => n.Position);
        BTreeAutoLayout.Layout(_asset, _measure, _headerHeight());
        var forward = new List<NodeMove>();
        var inverse = new List<NodeMove>();
        foreach (var n in _asset.Nodes)
        {
            var old = before[n.VisualId];
            if (old == n.Position) continue;
            forward.Add(new NodeMove(new NodeId(n.VisualId), n.Position));
            inverse.Add(new NodeMove(new NodeId(n.VisualId), old));
            n.Position = old;   // the command (below) applies the move — and records its inverse
        }
        if (forward.Count == 0) return false;
        var fwd = new GraphCommand.MoveNodes(forward);
        var inv = new GraphCommand.MoveNodes(inverse);
        if (Recorder != null) Recorder(fwd, inv, "Reset layout");
        else _sink.Apply(fwd);
        return true;
    }
}
