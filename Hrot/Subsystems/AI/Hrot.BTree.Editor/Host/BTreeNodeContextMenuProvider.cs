using System;
using System.Collections.Generic;
using Fbt;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Provides a right-click "Add Decorator →" submenu on any BTree node.
/// Each child item dispatches <see cref="GraphCommand.AddAttachment"/> through
/// the command sink, which <see cref="BTreeCommandSink.ApplyAddPill"/> translates
/// into a <c>BTreeEditorPill</c> on the asset.
///
/// <para>
/// When <see cref="Recorder"/> is set (by wiring <c>view.Execute</c> after the
/// <see cref="NodeEditor.Core.View.GraphView"/> is created), the add is routed
/// through the undo stack so that Ctrl+Z reverses it.  When <see cref="Recorder"/>
/// is null the operation falls back to the non-undoable <c>_sink.Apply</c> path
/// (defensive; should not occur in normal authoring).
/// </para>
/// </summary>
internal sealed class BTreeNodeContextMenuProvider : INodeContextMenuProvider
{
    private readonly IGraphCommandSink _sink;
    private readonly IGraphModel       _model;

    /// <summary>
    /// Optional recorder wired from the containing <see cref="NodeEditor.Core.View.GraphView"/>
    /// after it is created.  Signature matches <c>GraphView.Execute(fwd, inv, label)</c>.
    /// </summary>
    public Action<GraphCommand, GraphCommand, string>? Recorder { get; set; }

    public BTreeNodeContextMenuProvider(IGraphCommandSink sink, IGraphModel model)
    {
        _sink  = sink;
        _model = model;
    }

    public IReadOnlyList<ContextMenuItem> GetItemsFor(NodeId node, IReadOnlyList<NodeId> selection)
    {
        // Part 4 (L3 prevention): check whether this node already has a Repeater pill.
        bool hasRepeater = false;
        foreach (var att in _model.GetAttachmentsForNode(node))
        {
            if (att.HostProperties != null &&
                att.HostProperties.TryGetValue("decoratorType", out var dt) &&
                dt is NodeType nt && nt == NodeType.Repeater)
            {
                hasRepeater = true;
                break;
            }
        }

        var children = new List<ContextMenuItem>
        {
            MakeItem(node, "Inverter",      BTreeKinds.Inverter,     NodeType.Inverter),
            MakeItem(node, "Repeater",      BTreeKinds.Repeater,     NodeType.Repeater,  enabled: !hasRepeater),
            MakeItem(node, "Cooldown",      BTreeKinds.Cooldown,     NodeType.Cooldown),
            MakeItem(node, "Force Success", BTreeKinds.ForceSuccess, NodeType.ForceSuccess),
            MakeItem(node, "Force Failure", BTreeKinds.ForceFailure, NodeType.ForceFailure),
            MakeItem(node, "Until Success", BTreeKinds.UntilSuccess, NodeType.UntilSuccess),
            MakeItem(node, "Until Failure", BTreeKinds.UntilFailure, NodeType.UntilFailure),
        };

        return new[]
        {
            new ContextMenuItem("Add Decorator", () => { }, true, children),
        };
    }

    private ContextMenuItem MakeItem(
        NodeId node, string friendlyName, string kindId, NodeType nodeType,
        bool enabled = true)
    {
        return new ContextMenuItem(friendlyName, () =>
        {
            int stackIndex = _model.GetAttachmentsForNode(node).Count;
            var newId = IdGenerator.NewAttachmentId();
            var props = new Dictionary<string, object?> { ["decoratorType"] = nodeType };

            var fwd = new GraphCommand.AddAttachment(
                newId, node, AttachmentCategory.Decorator,
                Glyph: null, Label: friendlyName, Tooltip: null, stackIndex, props);

            var inv = new GraphCommand.RemoveAttachments(new[] { newId });

            if (Recorder != null)
                Recorder(fwd, inv, "Add Decorator");
            else
                _sink.Apply(fwd);
        }, enabled);
    }
}
