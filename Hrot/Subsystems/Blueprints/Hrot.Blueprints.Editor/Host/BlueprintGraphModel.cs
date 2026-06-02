using Hrot.Blueprints.Core.Assets;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <para>
/// <see cref="IGraphModel"/> that projects one <see cref="Graph"/> from a
/// <see cref="BlueprintAsset"/> onto the NodeEdit canvas.
/// </para>
/// <para>
/// Links in the Blueprint asset are stored on the <see cref="Graph.Links"/> list as
/// (<c>FromNodeId</c>, <c>FromPinId</c>, <c>ToNodeId</c>, <c>ToPinId</c>) records.
/// A stable <see cref="LinkId"/> is derived deterministically from the
/// <c>(FromPinId, ToPinId)</c> pair so that the id survives round-trips without
/// being stored in the asset.
/// </para>
/// <para>
/// Call <see cref="Rebuild"/> (or subscribe to a mutation source that calls it) and then
/// raise <see cref="Changed"/> to keep the canvas in sync with in-memory asset edits.
/// </para>
/// </summary>
public sealed class BlueprintGraphModel : IGraphModel
{
    private readonly BlueprintAsset _asset;
    private readonly Graph          _graph;

    // Projection caches (rebuilt when the asset graph mutates).
    private Dictionary<NodeId, INodeModel>  _nodes  = new();
    private Dictionary<LinkId, ILinkModel>  _links  = new();
    private Dictionary<PinId,  IPinModel>   _pins   = new();

    // ── ctor / init ──────────────────────────────────────────────────────────

    /// <param name="asset">The owning asset.</param>
    /// <param name="graph">The specific graph inside the asset to project.</param>
    public BlueprintGraphModel(BlueprintAsset asset, Graph graph)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Rebuild();
    }

    // ── IGraphModel ──────────────────────────────────────────────────────────

    public GraphId Id          => new(IdGenerator.Deterministic($"graph:{_asset.AssetId}:{_graph.Id}"));
    public string  DisplayName => _graph.Name;
    public GraphKindDescriptor Kind { get; } =
        new("EventGraph", "Event Graph", AllowsLatent: true, RequiresEntryNode: true);

    public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
    public IReadOnlyCollection<ILinkModel>    Links    => _links.Values;
    public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel?  FindNode(NodeId id)  => _nodes.TryGetValue(id,  out var v) ? v : null;
    public IPinModel?   FindPin(PinId id)    => _pins.TryGetValue(id,   out var v) ? v : null;
    public ILinkModel?  FindLink(LinkId id)  => _links.TryGetValue(id,  out var v) ? v : null;

    // ── mutation notification ────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the node/pin/link projection from the current asset state and
    /// fires <see cref="Changed"/>. Call this after any in-memory asset mutation.
    /// </summary>
    public void Rebuild()
    {
        var nodes  = new Dictionary<NodeId, INodeModel>();
        var pins   = new Dictionary<PinId,  IPinModel>();
        var links  = new Dictionary<LinkId, ILinkModel>();

        // Project nodes and their pins.
        foreach (var assetNode in _graph.Nodes)
        {
            var nodeModel = new BlueprintNodeModel(assetNode);
            nodes[nodeModel.Id] = nodeModel;
            foreach (var pin in nodeModel.Pins)
                pins[pin.Id] = pin;
        }

        // Project links — derive a stable LinkId from the (FromPinId, ToPinId) pair.
        foreach (var assetLink in _graph.Links)
        {
            var fromPin = new PinId(assetLink.FromPinId);
            var toPin   = new PinId(assetLink.ToPinId);
            var linkId  = MakeLinkId(assetLink.FromPinId, assetLink.ToPinId);
            links[linkId] = new BlueprintLinkModel(linkId, fromPin, toPin);
        }

        _nodes = nodes;
        _pins  = pins;
        _links = links;
    }

    /// <summary>Fires <see cref="Changed"/> with <see cref="GraphChangeKind.Wholesale"/>.</summary>
    public void NotifyChanged()
        => Changed?.Invoke(new GraphChangeNotification(GraphChangeKind.Wholesale, null, null, null, null));

    /// <summary>Rebuilds caches and fires <see cref="Changed"/>.</summary>
    public void RebuildAndNotify()
    {
        Rebuild();
        NotifyChanged();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a deterministic <see cref="LinkId"/> from the two pin guids.
    /// Same (from, to) pair always yields the same id.
    /// </summary>
    public static LinkId MakeLinkId(Guid fromPinId, Guid toPinId)
    {
        var key = $"link:{fromPinId:N}:{toPinId:N}";
        return new LinkId(IdGenerator.Deterministic(key));
    }
}
