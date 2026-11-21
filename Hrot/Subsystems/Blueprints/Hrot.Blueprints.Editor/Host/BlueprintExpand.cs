using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Transform;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.View;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-76 — the editor's <c>Expand Node</c>: inline a macro call's body at the call site.
///
/// <para>
/// ⭐ <b>The exact mirror of <see cref="BlueprintCollapse"/></b>, deliberately down to the shape: one
/// <c>Prepare</c> that mutates nothing, a forward/inverse pair built from a <b>snapshot</b>, and a
/// refusal that reaches the designer instead of failing silently. Everything Batch 34 learned about
/// collapse applies unchanged, because expansion is collapse run backwards.
/// </para>
///
/// <para>
/// ⛔⛔ <b>Why the snapshot, and not the "exact inverse" the old menu item built.</b>
/// <c>CanvasRenderer</c> shipped an undo that <i>predicted</i> the ids expansion would mint —
/// <c>node_exp1</c>/<c>node_exp2</c>, a scheme that exists <b>only in the NodeEdit demo's fake
/// backend</b> — and removed those two ids on undo. For a real macro, expansion produces N nodes with
/// entirely different ids, so that inverse removed two nodes which never existed and left the body
/// behind. ⭐ <b>A predicted inverse is wrong the moment the backend changes; a snapshot cannot be.</b>
/// </para>
///
/// <para>
/// ⚠ The snapshot also restores the call node <b>object</b>, so its node id and every
/// <c>DeterministicIds.PinId</c> derived from it survive an expand→undo cycle. Breakpoints and debug
/// map entries follow them.
/// </para>
/// </summary>
internal static class BlueprintExpand
{
    /// <summary>
    /// Builds the forward/inverse pair for expanding <paramref name="node"/>, or explains the refusal.
    /// <b>Nothing is mutated</b> until <see cref="ExpandPreparation.Forward"/> runs.
    /// </summary>
    public static ExpandPreparation Prepare(BlueprintAsset asset, Graph host, Node node)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(node);

        // ⭐ Probe on a throwaway copy so a refusal costs the live graph nothing. MacroExpander
        // mutates in place by design (the editor holds live references to `host`), so the only way
        // to ask "would this work?" without a second copy of the resolution rules is to try it on a
        // clone — and duplicating those rules is the BP-69 mistake this batch exists to avoid.
        var probeHost = CloneGraphShallow(host);
        var probeNode = probeHost.Nodes.FirstOrDefault(n => n.Id == node.Id);
        if (probeNode is null)
            return ExpandPreparation.Refused(new MacroExpander.ExpandRefusal(
                MacroExpander.RefusalCodes.NotAMacroCall,
                "That node is not in this graph."));

        var probeAsset = ShallowAssetWith(asset, host, probeHost);
        var refusal = MacroExpander.TryExpand(probeAsset, probeHost, probeNode);
        if (refusal is not null)
            return ExpandPreparation.Refused(refusal);

        var beforeNodes = host.Nodes.ToList();
        // ⚠⚠ The links are COPIED, the nodes are not, and the asymmetry is the whole point.
        // `Link` is a mutable class and the splice rules rewrite endpoints IN PLACE
        // (`link.ToPinId = …`), so a shallow `host.Links.ToList()` would hand the probe the very
        // objects the "before" snapshot is made of — the probe would silently rewrite the state undo
        // is supposed to restore. `Node` must stay shared: restoring the ORIGINAL objects is what
        // preserves the call node's id and every pin GUID derived from it.
        var beforeLinks = host.Links.Select(CopyLink).ToList();
        var afterNodes  = probeHost.Nodes.ToList();
        var afterLinks  = probeHost.Links.ToList();

        void Forward()
        {
            host.Nodes = afterNodes.ToList();
            host.Links = afterLinks.ToList();
            CollapseEmitter.RebuildLinkedToIds(host);
        }

        void Inverse()
        {
            host.Nodes = beforeNodes.ToList();
            host.Links = beforeLinks.ToList();
            // ⚠ Mandatory: Pin.LinkedToIds is a denormalised mirror of the link list, and the forward
            // rewrote it on node objects this restores. Same reason as BlueprintCollapse's inverse.
            // (CollapseEmitter's copy, public since Batch 34 for exactly this — one rebuild, not two.)
            CollapseEmitter.RebuildLinkedToIds(host);
        }

        return ExpandPreparation.Ready(Forward, Inverse);
    }

    /// <summary>
    /// Prepares and performs an expansion, reporting a refusal to the designer rather than doing
    /// nothing quietly (Q26-B2 — the item is offered whenever a node is selected and refuses on
    /// invoke, so <c>NodeEditor.UI</c> needs no blueprint vocabulary to gate it).
    /// </summary>
    /// <param name="view">
    /// When supplied, the gesture is recorded as <b>one</b> undo entry. Null applies the forward
    /// directly — the sink's path, where the stack has already recorded the pair.
    /// </param>
    public static ExpandPreparation Run(
        GraphView?         view,
        BlueprintAsset     asset,
        Graph              host,
        Node               node,
        Action?            markDirty  = null,
        IEditorIndicators? indicators = null)
    {
        var prep = Prepare(asset, host, node);

        if (prep.IsRefused)
        {
            indicators?.Notify(prep.ToNotification());
            return prep;
        }

        const string Label = "Expand Node";
        if (view is not null)
            view.Execute(new BlueprintEditCommand(Label, prep.Forward!),
                         new BlueprintEditCommand(Label, prep.Inverse!),
                         Label);
        else
            prep.Forward!();

        markDirty?.Invoke();
        return prep;
    }

    /// <summary>
    /// A graph carrying the SAME node objects in fresh lists. ⚠ Shallow on purpose: the probe only
    /// needs somewhere to put spliced clones without touching the live lists, and the nodes it
    /// carries forward are the originals — which is exactly what makes the resulting "after" lists
    /// usable as the forward's payload, with the untouched host nodes keeping their identity.
    /// </summary>
    private static Graph CloneGraphShallow(Graph graph)
        => graph.WithNodesAndLinks(graph.Nodes.ToList(), graph.Links.Select(CopyLink).ToList());

    /// <summary>
    /// A fresh <see cref="Link"/> with the same endpoints. ⚠ Needed because the splice rewires by
    /// mutating link objects rather than replacing them, so sharing one between the probe and the
    /// snapshot lets the probe edit the past.
    /// </summary>
    private static Link CopyLink(Link l) => new()
    {
        FromNodeId = l.FromNodeId, FromPinId = l.FromPinId,
        ToNodeId   = l.ToNodeId,   ToPinId   = l.ToPinId,
        Waypoints  = l.Waypoints is null ? null : l.Waypoints.ToList(),
    };

    /// <summary>
    /// The asset with <paramref name="host"/> swapped for <paramref name="replacement"/>, so the
    /// probe resolves macro targets against the real graph list without the live host in it.
    /// </summary>
    private static BlueprintAsset ShallowAssetWith(BlueprintAsset asset, Graph host, Graph replacement)
    {
        var copy = new BlueprintAsset
        {
            AssetId  = asset.AssetId,
            Name     = asset.Name,
            Dispatch = asset.Dispatch,
            Header   = asset.Header,
        };
        foreach (var g in asset.Graphs)
            copy.Graphs.Add(ReferenceEquals(g, host) ? replacement : g);
        return copy;
    }
}

/// <summary>The outcome of <see cref="BlueprintExpand.Prepare"/>.</summary>
internal sealed class ExpandPreparation
{
    private ExpandPreparation(MacroExpander.ExpandRefusal? refusal, Action? forward, Action? inverse)
    {
        Refusal = refusal;
        Forward = forward;
        Inverse = inverse;
    }

    public static ExpandPreparation Refused(MacroExpander.ExpandRefusal refusal)
        => new(refusal, null, null);

    public static ExpandPreparation Ready(Action forward, Action inverse)
        => new(null, forward, inverse);

    public MacroExpander.ExpandRefusal? Refusal { get; }
    public Action? Forward { get; }
    public Action? Inverse { get; }
    public bool IsRefused => Forward is null;

    /// <summary>The refusal as a toast — drawn since BP-223 supplied the missing consumer.</summary>
    public EditorNotification ToNotification()
        => new(
            Id:          Refusal?.Code ?? "expand.refused",
            Severity:    NotificationSeverity.Warning,
            Title:       "Cannot expand this node",
            Body:        Refusal?.Message,
            AutoDismiss: TimeSpan.FromSeconds(8),
            Actions:     null);
}
