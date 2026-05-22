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

    /// <summary>
    /// Raised when graph data changes externally. The editor subscribes and
    /// updates view state (selection, viewport hold, badges, undo invalidation).
    /// </summary>
    event Action<GraphChangeNotification>? Changed;
}

/// <summary>Descriptor for the kind of graph (event/function/macro/…).</summary>
public sealed record GraphKindDescriptor(
    string Id,
    string DisplayName,
    bool AllowsLatent,
    bool RequiresEntryNode);

/// <summary>Payload describing what changed in a graph.</summary>
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
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
    Wholesale,
}
