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
/// </summary>
internal sealed class BTreeNodeContextMenuProvider : INodeContextMenuProvider
{
    private readonly IGraphCommandSink _sink;
    private readonly IGraphModel       _model;

    public BTreeNodeContextMenuProvider(IGraphCommandSink sink, IGraphModel model)
    {
        _sink  = sink;
        _model = model;
    }

    public IReadOnlyList<ContextMenuItem> GetItemsFor(NodeId node, IReadOnlyList<NodeId> selection)
    {
        var children = new List<ContextMenuItem>
        {
            MakeItem(node, "Inverter",      BTreeKinds.Inverter,     NodeType.Inverter),
            MakeItem(node, "Repeater",      BTreeKinds.Repeater,     NodeType.Repeater),
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

    private ContextMenuItem MakeItem(NodeId node, string friendlyName, string kindId, NodeType nodeType)
    {
        return new ContextMenuItem(friendlyName, () =>
        {
            int stackIndex = _model.GetAttachmentsForNode(node).Count;
            var props = new Dictionary<string, object?> { ["decoratorType"] = nodeType };
            _sink.Apply(new GraphCommand.AddAttachment(
                IdGenerator.NewAttachmentId(), node, AttachmentCategory.Decorator,
                Glyph: null, Label: friendlyName, Tooltip: null, stackIndex, props));
        });
    }
}
