using System;
using System.Collections.Generic;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// ⭐ CE-1003 (Q84 B) — HSM items in the canvas's node context menu. Mirrors <c>BTreeNodeContextMenuProvider</c>:
/// items execute through <see cref="Recorder"/> (the GraphView's <c>Execute(fwd, inv, label)</c>) so they are undoable.
/// <para>"Set as Initial State" is the editor's FIRST way to choose a plain composite's start state — the inspector's
/// flag was display-only and the region facet took a typed name.</para>
/// </summary>
internal sealed class HsmNodeContextMenuProvider : INodeContextMenuProvider
{
    private readonly HsmAsset _asset;

    public HsmNodeContextMenuProvider(HsmAsset asset) => _asset = asset;

    /// <summary>Wired from the GraphView after it is created. Signature matches <c>GraphView.Execute</c>.</summary>
    public Action<GraphCommand, GraphCommand, string>? Recorder { get; set; }

    public IReadOnlyList<ContextMenuItem> GetItemsFor(NodeId node, IReadOnlyList<NodeId> selection)
    {
        var state = _asset.FindStateByStableId(node.Value);
        if (state is null || ReferenceEquals(state, _asset.RootState))
            return Array.Empty<ContextMenuItem>();

        var items = new List<ContextMenuItem>();
        if (Recorder is not null)
            items.Add(new ContextMenuItem("Set as Initial State", () => SetInitial(state), Enabled: !_asset.IsStartState(state)));
        // ⭐ CE-1323 (DBG-13) — the state's breakpoints, on the state itself (they were only on its gutter dot)
        if (Breakpoints is { } manager)
        {
            var collector = new HsmContextMenuItemCollector();
            Hrot.Hsm.Editor.Debug.HsmBreakpointMenuPopulator.PopulateStateMenu(_asset, state, collector, manager);
            items.AddRange(collector.Items);
        }
        return items;
    }

    /// <summary>⭐ CE-1323 — the breakpoint manager; null = no breakpoint items.</summary>
    public Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? Breakpoints { get; set; }

    internal void SetInitial(StateNode state)
    {
        var previous = _asset.CurrentStartStateFor(state);
        var fwd = new GraphCommand.SetNodeProperty(new NodeId(state.StableId), "isInitial", true);
        GraphCommand inv = previous != null
            ? new GraphCommand.SetNodeProperty(new NodeId(previous.StableId), "isInitial", true)
            : new GraphCommand.SetNodeProperty(new NodeId(state.StableId), "isInitial", false);
        Recorder?.Invoke(fwd, inv, "Set Initial State");
    }
}
