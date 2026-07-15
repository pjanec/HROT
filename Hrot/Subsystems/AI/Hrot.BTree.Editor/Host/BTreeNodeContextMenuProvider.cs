using System;
using System.Collections.Generic;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Provides a right-click "Add Decorator →" submenu on any BTree node, plus (Phase D / AIE-053)
/// an "Open Blueprint" item on composed AiPrimitive nodes.
/// Each decorator child item dispatches <see cref="GraphCommand.AddAttachment"/> through
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
/// <para>
/// The "Open Blueprint" item only appears when <paramref name="asset"/>, <paramref name="assetCatalog"/>,
/// and <paramref name="openAsset"/> (ctor params) are all supplied AND the node under the cursor is a
/// composed AiPrimitive node (Action/Condition with <c>DelegateShape == AiPrimitiveTickCore</c>) whose
/// <c>MethodFqn</c> resolves to a Blueprint asset via <see cref="ComposedBlueprintResolver"/>. All three
/// are optional so existing "Add Decorator"-only call sites/tests keep working unchanged.
/// </para>
/// </summary>
internal sealed class BTreeNodeContextMenuProvider : INodeContextMenuProvider
{
    private readonly IGraphCommandSink _sink;
    private readonly IGraphModel       _model;
    private readonly BehaviorTreeAsset? _asset;
    private readonly IAssetCatalog?     _assetCatalog;
    private readonly Action<IEditableAsset>? _openAsset;

    /// <summary>
    /// Optional recorder wired from the containing <see cref="NodeEditor.Core.View.GraphView"/>
    /// after it is created.  Signature matches <c>GraphView.Execute(fwd, inv, label)</c>.
    /// </summary>
    public Action<GraphCommand, GraphCommand, string>? Recorder { get; set; }

    public BTreeNodeContextMenuProvider(
        IGraphCommandSink sink,
        IGraphModel model,
        BehaviorTreeAsset? asset = null,
        IAssetCatalog? assetCatalog = null,
        Action<IEditableAsset>? openAsset = null)
    {
        _sink         = sink;
        _model        = model;
        _asset        = asset;
        _assetCatalog = assetCatalog;
        _openAsset    = openAsset;
    }

    /// <summary>
    /// Phase D (AIE-053) testable core: resolves the node under <paramref name="node"/> to its
    /// composed Blueprint asset, or <see langword="null"/> when the node is not a composed
    /// AiPrimitive node, the reference is dangling, or the provider wasn't constructed with an
    /// asset/catalog (headless "Add Decorator"-only call sites). Kept separate from
    /// <see cref="GetItemsFor"/>/ImGui menu registration so it can be unit-tested directly.
    /// </summary>
    internal IEditableAsset? ResolveOpenBlueprintTarget(NodeId node)
    {
        if (_asset is null || _assetCatalog is null)
            return null;

        var editorNode = _asset.FindNode(node.Value);
        if (editorNode is null)
            return null;

        string? methodFqn = editorNode.KernelType switch
        {
            NodeType.Action when editorNode.Action?.DelegateShape == BTreeActionDelegateShape.AiPrimitiveTickCore
                => editorNode.Action.MethodFqn,
            NodeType.Condition when editorNode.Condition?.DelegateShape == BTreeActionDelegateShape.AiPrimitiveTickCore
                => editorNode.Condition.MethodFqn,
            _ => null,
        };
        if (methodFqn is null)
            return null;

        return ComposedBlueprintResolver.Resolve(methodFqn, _assetCatalog);
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

        var items = new List<ContextMenuItem>
        {
            new ContextMenuItem("Add Decorator", () => { }, true, children),
        };

        // Phase D (AIE-053): "Open Blueprint" — only shown for a composed AiPrimitive node whose
        // MethodFqn resolves cleanly to a Blueprint asset.
        var openTarget = ResolveOpenBlueprintTarget(node);
        if (openTarget != null && _openAsset != null)
        {
            items.Add(new ContextMenuItem("Open Blueprint", () => _openAsset(openTarget), true));
        }

        return items;
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
