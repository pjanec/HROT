using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Model;

// ── BTreeNodeModel ────────────────────────────────────────────────────────────

/// <summary>
/// Read-only <see cref="INodeModel"/> adapter over a <see cref="BTreeEditorNode"/>.
/// Provides two implicit exec pins per node (Output = child's "up-link",
/// Input = parent's "down-link") matching the reversed-pin convention used
/// by <see cref="BTreeGraphModel"/>.
/// </summary>
internal sealed class BTreeNodeModel : INodeModel
{
    private readonly BTreeEditorNode _node;
    private readonly BTreePinModel[] _pins;

    internal BTreeNodeModel(BTreeEditorNode node)
    {
        _node = node;
        // Reversed-pin convention: child output → parent input.
        // Pin 0 = Output (the child's "send to parent" pin)
        // Pin 1 = Input  (the parent's "receive from child" pin)
        _pins = new[]
        {
            new BTreePinModel(
                id:        new PinId(node.OutputPinId),
                owner:     new NodeId(node.VisualId),
                direction: PinDirection.Output),
            new BTreePinModel(
                id:        new PinId(node.InputPinId),
                owner:     new NodeId(node.VisualId),
                direction: PinDirection.Input),
        };
    }

    public NodeId              Id            => new((_node.VisualId));
    public NodeKindKey         Kind          => new(_node.KernelType.ToString());
    public string              Title         => string.IsNullOrEmpty(_node.DisplayLabel)
                                                   ? _node.KernelType.ToString()
                                                   : _node.DisplayLabel;
    public string?             Subtitle      => null;
    public NodeCategory        Category      => NodeCategory.FlowControl;
    public Vector2             Position      => _node.Position;
    public Vector2?            SizeOverride  => null;
    public NodeState           State         => NodeState.Normal;
    public string?             StatusTooltip => null;
    public bool                IsCollapsed   => false;
    public bool                ShowAdvancedPins => false;
    public IReadOnlyList<IPinModel> Pins     => _pins;
}

// ── BTreePinModel ─────────────────────────────────────────────────────────────

internal sealed class BTreePinModel : IPinModel
{
    public BTreePinModel(PinId id, NodeId owner, PinDirection direction)
    {
        Id          = id;
        OwnerNodeId = owner;
        Direction   = direction;
    }

    public PinId         Id          { get; }
    public NodeId        OwnerNodeId { get; }
    public string        Label       => string.Empty;
    public PinDirection  Direction   { get; }
    public PinKind       Kind        => PinKind.Exec;
    public TypeKey?      Type        => null;
    public PinShape      Shape       => PinShape.Circle;
    public bool          IsAdvanced  => false;
    public bool          IsOptional  => false;
    public string?       Tooltip     => null;
    public IPinDefaultValue? Default => null;
}

// ── BTreePillAttachmentModel ──────────────────────────────────────────────────

internal sealed class BTreePillAttachmentModel : IAttachmentModel
{
    private readonly BTreeEditorPill _pill;

    internal BTreePillAttachmentModel(BTreeEditorPill pill)
    {
        _pill = pill;
    }

    public AttachmentId      Id         => new(_pill.VisualId);
    public NodeId            HostNodeId => new(_pill.HostNodeVisualId);
    public AttachmentCategory Category  => AttachmentCategory.Decorator;
    public string?           Glyph     => null;
    public string?           Label     => _pill.DecoratorType.ToString();
    public string?           Tooltip   => _pill.Comment;
    public AttachmentState   State     => AttachmentState.Normal;
    public int               StackIndex => _pill.StackIndex;
}

// ── BTreeGraphModel ───────────────────────────────────────────────────────────

/// <summary>
/// <see cref="IGraphModel"/> implementation backed by a <see cref="BehaviorTreeAsset"/>.
/// <para>
/// The BTree canvas uses the "reversed-pin" convention: each node exposes
/// an <em>Output</em> pin (child's link up to its parent) and an <em>Input</em>
/// pin (parent's link down from its child).  Links therefore run
/// child.OutputPin → parent.InputPin, matching the NodeEdit exec many-to-one rule
/// while visually rendering parent→child.
/// </para>
/// <para>
/// This is the missing IGraphModel bridge the batch instructions described —
/// BehaviorTreeAsset does not itself implement IGraphModel; this class provides it.
/// </para>
/// </summary>
public sealed class BTreeGraphModel : IGraphModel
{
    private readonly BehaviorTreeAsset _asset;

    // Cached node and attachment adapters.  Rebuilt when the asset fires Changed.
    private readonly Dictionary<NodeId,       BTreeNodeModel>           _nodeCache       = new();
    private readonly Dictionary<PinId,        BTreePinModel>            _pinCache        = new();
    private readonly Dictionary<AttachmentId, BTreePillAttachmentModel> _attachmentCache = new();

    /// <summary>
    /// Constructs a graph model view over <paramref name="asset"/>.
    /// Subscribes to <see cref="BehaviorTreeAsset.Changed"/> to keep caches current.
    /// </summary>
    public BTreeGraphModel(BehaviorTreeAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _asset.Changed += OnAssetChanged;
        BuildCaches();
    }

    private void OnAssetChanged()
    {
        BuildCaches();
        Changed?.Invoke(new GraphChangeNotification(
            GraphChangeKind.Wholesale, null, null, null, "BehaviorTreeAsset changed"));
    }

    private void BuildCaches()
    {
        _nodeCache.Clear();
        _pinCache.Clear();
        _attachmentCache.Clear();

        foreach (var node in _asset.Nodes)
        {
            var model = new BTreeNodeModel(node);
            _nodeCache[model.Id] = model;
            foreach (var pin in model.Pins)
                _pinCache[pin.Id] = (BTreePinModel)pin;
        }

        foreach (var pill in _asset.Pills)
        {
            var model = new BTreePillAttachmentModel(pill);
            _attachmentCache[model.Id] = model;
        }
    }

    // ── IGraphModel ────────────────────────────────────────────────────────────

    public GraphId              Id          => new(_asset.AssetId);
    public string               DisplayName => _asset.Name;
    public GraphKindDescriptor  Kind        { get; } =
        new("BTreeGraph", "Behavior Tree", AllowsLatent: false, RequiresEntryNode: true);

    public IReadOnlyCollection<INodeModel>       Nodes    => _nodeCache.Values;
    public IReadOnlyCollection<ILinkModel>       Links    => Array.Empty<ILinkModel>();
    public IReadOnlyCollection<ICommentModel>    Comments => Array.Empty<ICommentModel>();
    public IReadOnlyCollection<IAttachmentModel> Attachments => _attachmentCache.Values;

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel? FindNode(NodeId id) =>
        _nodeCache.TryGetValue(id, out var n) ? n : null;

    public IPinModel? FindPin(PinId id) =>
        _pinCache.TryGetValue(id, out var p) ? p : null;

    public ILinkModel? FindLink(LinkId id) => null;

    public IAttachmentModel? FindAttachment(AttachmentId id) =>
        _attachmentCache.TryGetValue(id, out var a) ? a : null;

    public IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId)
    {
        var result = new List<IAttachmentModel>();
        foreach (var a in _attachmentCache.Values)
            if (a.HostNodeId == hostId)
                result.Add(a);
        result.Sort((x, y) => x.StackIndex.CompareTo(y.StackIndex));
        return result;
    }
}
