using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Read-only view of a graph's data. Implemented by the host. The editor
/// never mutates this directly; mutations go through <see cref="IGraphCommandSink"/>.
/// </summary>
public interface IGraphModel
{
    /// <summary>Stable identifier for this graph.</summary>
    GraphId Id { get; }

    /// <summary>Display name shown in tabs and breadcrumbs.</summary>
    string DisplayName { get; }

    /// <summary>Descriptor for the kind of graph (event graph, function body, …).</summary>
    GraphKindDescriptor Kind { get; }

    /// <summary>All nodes currently in this graph.</summary>
    IReadOnlyCollection<INodeModel> Nodes { get; }

    /// <summary>All links currently in this graph.</summary>
    IReadOnlyCollection<ILinkModel> Links { get; }

    /// <summary>All comment boxes currently in this graph.</summary>
    IReadOnlyCollection<ICommentModel> Comments { get; }

    /// <summary>Find a node by id, or null if not present.</summary>
    INodeModel? FindNode(NodeId id);

    /// <summary>Find a pin by id, or null if not present.</summary>
    IPinModel? FindPin(PinId id);

    /// <summary>Find a link by id, or null if not present.</summary>
    ILinkModel? FindLink(LinkId id);

    /// <summary>All attachments currently in this graph.</summary>
    IReadOnlyCollection<IAttachmentModel> Attachments
        => Array.Empty<IAttachmentModel>();

    /// <summary>Find an attachment by id, or null if not present.</summary>
    IAttachmentModel? FindAttachment(AttachmentId id) => null;

    /// <summary>
    /// Returns all attachments whose host is the given node, ordered by StackIndex ascending.
    /// Returns an empty list if the node has no attachments or does not exist.
    /// </summary>
    IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId)
        => Array.Empty<IAttachmentModel>();

    /// <summary>
    /// Raised when graph data changes externally. The editor subscribes and
    /// updates view state (selection, viewport hold, badges, undo invalidation).
    /// </summary>
    /// <summary>
    /// CE-1000/CE-1001 — in <see cref="LinkRouting.NodeToNode"/> graphs, the pin a whole-node link attaches to on
    /// <paramref name="node"/> (an <see cref="PinDirection.Output"/> pin for the source end, <see cref="PinDirection.Input"/>
    /// for the target end). ⚠ It must also answer for a node id that is ABOUT TO BE CREATED — the "drop on empty
    /// canvas" batch adds the node and the link in one undo step — so a host whose pin ids derive from the node id
    /// overrides this; the default reads the existing node's pins.
    /// </summary>
    PinId? NodeLinkPin(NodeId node, PinDirection direction)
        => FindNode(node)?.Pins.FirstOrDefault(p => p.Direction == direction)?.Id;

    event Action<GraphChangeNotification>? Changed;
}

/// <summary>Descriptor for the kind of graph (event/function/macro/…).</summary>
public sealed record GraphKindDescriptor(
    string Id,
    string DisplayName,
    bool AllowsLatent,
    bool RequiresEntryNode)
{
    /// <summary>
    /// Controls where pins are placed on the node: Horizontal (input left / output right)
    /// or Vertical (output top / input bottom).  Default is Horizontal.
    /// </summary>
    public PinOrientation Orientation { get; init; } = PinOrientation.Horizontal;

    /// <summary>
    /// ⭐ CE-1000 — how links are drawn and started. <see cref="LinkRouting.PinWires"/> (default) is the
    /// Blueprint/BTree pin-to-pin wire. <see cref="LinkRouting.NodeToNode"/> draws state-machine arrows from node
    /// border to node border, and a link can be started by dragging from a node's border band or Shift-dragging the
    /// node. The link model is unchanged (still pin to pin); only geometry and gestures differ.
    /// </summary>
    public LinkRouting Routing { get; init; } = LinkRouting.PinWires;

    /// <summary>What one link is called in menus ("Add {LinkDisplayName}"), e.g. "Transition".</summary>
    public string LinkDisplayName { get; init; } = "Link";
}

/// <summary>How a graph's links are drawn and started (CE-1000).</summary>
public enum LinkRouting
{
    /// <summary>Pin-to-pin S-curve wires (Blueprint, BTree).</summary>
    PinWires = 0,

    /// <summary>Border-to-border arrows on a gentle arc (state machines).</summary>
    NodeToNode = 1,
}

/// <summary>Controls which node edges pins are placed on.</summary>
public enum PinOrientation
{
    /// <summary>Input pins on the left edge, output pins on the right edge (default).</summary>
    Horizontal = 0,

    /// <summary>Output pins on the top edge, input pins on the bottom edge.</summary>
    Vertical = 1,
}

/// <summary>Payload describing what changed in a graph.</summary>
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    IReadOnlySet<AttachmentId>? AffectedAttachments,
    string? Reason);

/// <summary>Coarse classification of a graph change.</summary>
public enum GraphChangeKind
{
    NodesAdded,
    NodesRemoved,
    NodesModified,
    NodesMoved,
    LinksAdded,
    LinksRemoved,
    VariablesChanged,
    AttachmentsAdded,
    AttachmentsRemoved,
    AttachmentsModified,
    Wholesale,
}
