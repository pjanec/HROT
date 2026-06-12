using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Validation;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Model;

// ── BTreeParentChildLink ──────────────────────────────────────────────────────

/// <summary>
/// <see cref="ILinkModel"/> adapter for a single parent↔child edge in a BTree.
/// Follows the reversed-pin convention: FromPin = child.OutputPinId, ToPin = parent.InputPinId.
/// LinkId is deterministically derived from the child's VisualId so it survives reload.
/// </summary>
internal sealed class BTreeParentChildLink : ILinkModel
{
    // XOR constant for the link id — distinct from pin-id XOR constants.
    private static readonly (ulong hi, ulong lo) LinkIdXorKey =
        (0xCC_00_00_00_00_00_00_01UL, 0x00_00_00_00_00_00_00_05UL);

    private static Guid XorGuid(Guid g, ulong hi, ulong lo)
    {
        var bytes = g.ToByteArray();
        var hiBytes = BitConverter.GetBytes(hi);
        var loBytes = BitConverter.GetBytes(lo);
        for (int i = 0; i < 8; i++) bytes[i]     ^= hiBytes[i];
        for (int i = 0; i < 8; i++) bytes[i + 8] ^= loBytes[i];
        return new Guid(bytes);
    }

    internal BTreeParentChildLink(BTreeEditorNode child, BTreeEditorNode parent)
    {
        // Id is keyed on child VisualId (one link per child)
        Id      = new LinkId(XorGuid(child.VisualId, LinkIdXorKey.hi, LinkIdXorKey.lo));
        FromPin = new PinId(child.OutputPinId);
        ToPin   = new PinId(parent.InputPinId);
    }

    public LinkId                     Id        { get; }
    public PinId                      FromPin   { get; }
    public PinId                      ToPin     { get; }
    public LinkStyle                  Style     => LinkStyle.Solid;
    public IReadOnlyList<Vector2>     Waypoints => Array.Empty<Vector2>();
}

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
    private readonly NodeState       _state;
    private readonly string?         _statusTooltip;

    internal BTreeNodeModel(BTreeEditorNode node, NodeState state, string? statusTooltip)
    {
        _node = node;
        _state = state;
        _statusTooltip = statusTooltip;
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
    public NodeCategory        Category      => _node.KernelType switch
        {
            Fbt.NodeType.Action           => NodeCategory.Function,
            Fbt.NodeType.Wait             => NodeCategory.Function,
            Fbt.NodeType.Condition        => NodeCategory.Pure,
            Fbt.NodeType.Subtree          => NodeCategory.Macro,
            // Composites (Root, Sequence, Selector, ObserverSelector, Parallel)
            // and unknown types map to FlowControl.
            _                             => NodeCategory.FlowControl,
        };
    public Vector2             Position      => _node.Position;
    public Vector2?            SizeOverride  => null;
    public NodeState           State         => _state;
    public string?             StatusTooltip => _statusTooltip;
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

    public string? Glyph => _pill.DecoratorType switch
    {
        NodeType.Inverter     => "!",
        NodeType.Repeater     => "R",
        NodeType.Cooldown     => "C",
        NodeType.ForceSuccess => "S",
        NodeType.ForceFailure => "F",
        NodeType.UntilSuccess => "U+",
        NodeType.UntilFailure => "U-",
        _                     => "?",
    };

    public string? Label => _pill.DecoratorType switch
    {
        NodeType.Repeater => $"x{_pill.IntParam ?? 1}",
        NodeType.Cooldown => FormattableString.Invariant($"{_pill.FloatParam ?? 0f}s"),
        NodeType.Inverter     => nameof(NodeType.Inverter),
        NodeType.ForceSuccess => nameof(NodeType.ForceSuccess),
        NodeType.ForceFailure => nameof(NodeType.ForceFailure),
        NodeType.UntilSuccess => nameof(NodeType.UntilSuccess),
        NodeType.UntilFailure => nameof(NodeType.UntilFailure),
        _                     => _pill.DecoratorType.ToString(),
    };

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

    // Cached node, link, and attachment adapters.  Rebuilt when the asset fires Changed.
    private readonly Dictionary<NodeId,       BTreeNodeModel>           _nodeCache       = new();
    private readonly Dictionary<PinId,        BTreePinModel>            _pinCache        = new();
    private readonly Dictionary<LinkId,       BTreeParentChildLink>     _linkCache       = new();
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
        _linkCache.Clear();
        _attachmentCache.Clear();

        // ── Per-node diagnostics (canvas node-state projection) ──────────────
        var nodeDiagnostics = new Dictionary<Guid, (NodeState State, string Tooltip)>();
        foreach (var d in new BTreeValidator().Validate(_asset))
        {
            if (d.VisualId == Guid.Empty) continue; // tree-level diagnostic → no single node

            NodeState severity = d.Severity switch
            {
                BTreeDiagnosticSeverity.Error   => NodeState.Error,
                BTreeDiagnosticSeverity.Warning => NodeState.Warning,
                _                               => NodeState.Normal,
            };
            if (severity == NodeState.Normal) continue;

            if (!nodeDiagnostics.TryGetValue(d.VisualId, out var existing))
            {
                nodeDiagnostics[d.VisualId] = (severity, d.Message);
            }
            else if (severity == NodeState.Error && existing.State != NodeState.Error)
            {
                // Error wins over Warning; store the error's message.
                nodeDiagnostics[d.VisualId] = (severity, d.Message);
            }
        }

        // Build a VisualId→BTreeEditorNode lookup for link projection.
        var byVisualId = new Dictionary<Guid, BTreeEditorNode>(_asset.Nodes.Count);
        foreach (var node in _asset.Nodes)
        {
            var (state, tooltip) = nodeDiagnostics.TryGetValue(node.VisualId, out var diag)
                ? (diag.State, diag.Tooltip)
                : (NodeState.Normal, (string?)null);

            var model = new BTreeNodeModel(node, state, tooltip);
            _nodeCache[model.Id] = model;
            foreach (var pin in model.Pins)
                _pinCache[pin.Id] = (BTreePinModel)pin;
            byVisualId[node.VisualId] = node;
        }

        // Project each parent→child edge as child.OutputPin → parent.InputPin.
        foreach (var parent in _asset.Nodes)
        {
            foreach (var childId in parent.ChildVisualIds)
            {
                if (!byVisualId.TryGetValue(childId, out var child))
                    continue;
                var link = new BTreeParentChildLink(child, parent);
                _linkCache[link.Id] = link;
            }
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
    public IReadOnlyCollection<ILinkModel>       Links    => _linkCache.Values;
    public IReadOnlyCollection<ICommentModel>    Comments => Array.Empty<ICommentModel>();
    public IReadOnlyCollection<IAttachmentModel> Attachments => _attachmentCache.Values;

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel? FindNode(NodeId id) =>
        _nodeCache.TryGetValue(id, out var n) ? n : null;

    public IPinModel? FindPin(PinId id) =>
        _pinCache.TryGetValue(id, out var p) ? p : null;

    public ILinkModel? FindLink(LinkId id) =>
        _linkCache.TryGetValue(id, out var link) ? link : null;

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
