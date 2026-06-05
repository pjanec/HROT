using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.NodeDrawers;
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
/// <para>
/// Pin hydration is <b>projection-only</b>: loaded assets that have <c>"Pins": []</c>
/// receive canonical pins derived from <see cref="NodePinSchema"/>.  Pin GUIDs for
/// connected pins are bound from the incident link's <c>FromPinId</c>/<c>ToPinId</c>
/// fields; unconnected pins receive deterministic GUIDs.  Nothing is written back
/// to the asset or to disk.
/// </para>
/// </summary>
public sealed class BlueprintGraphModel : IGraphModel
{
    private readonly BlueprintAsset    _asset;
    private readonly Graph             _graph;
    private readonly NodeKindRegistry? _kindRegistry;
    private readonly IChannelCommandCatalog? _channelCommands;
    private readonly Func<Guid, BlueprintSignature?>? _peerSignatureLookup;

    // Projection caches (rebuilt when the asset graph mutates).
    private Dictionary<NodeId, INodeModel>  _nodes  = new();
    private Dictionary<LinkId, ILinkModel>  _links  = new();
    private Dictionary<PinId,  IPinModel>   _pins   = new();

    // ── ctor / init ──────────────────────────────────────────────────────────

    /// <param name="asset">The owning asset.</param>
    /// <param name="graph">The specific graph inside the asset to project.</param>
    /// <param name="kindRegistry">
    /// Optional palette registry used by <see cref="NodePinSchema"/> to look up canonical
    /// pin lists for node kinds registered at editor startup (e.g. WhenNode, ReadEqsResult).
    /// Pass <see langword="null"/> in unit tests that don't exercise those kinds.
    /// </param>
    /// <param name="channelCommands">
    /// Optional channel-command catalog forwarded to <see cref="NodePinSchema.GetCanonicalPins"/>
    /// so <see cref="ChannelCommandNode"/> projects its parameter data-IN pins from the matching
    /// catalog entry's params type.  When <see langword="null"/> channel-command nodes fall back
    /// to exec-only (the prior behavior).
    /// </param>
    /// <param name="peerSignatureLookup">
    /// Optional delegate that resolves a peer asset's <see cref="BlueprintSignature"/> by GUID.
    /// Forwarded to <see cref="NodePinSchema.GetCanonicalPins"/> so
    /// <see cref="CallPeerBlueprintNode"/> can project typed argument pins from the peer's
    /// exported function signature.  When <see langword="null"/> those nodes fall back to the
    /// static exec In/Out + Return:System.Object shape.
    /// </param>
    public BlueprintGraphModel(
        BlueprintAsset    asset,
        Graph             graph,
        NodeKindRegistry? kindRegistry = null,
        IChannelCommandCatalog? channelCommands = null,
        Func<Guid, BlueprintSignature?>? peerSignatureLookup = null)
    {
        _asset                = asset ?? throw new ArgumentNullException(nameof(asset));
        _graph                = graph ?? throw new ArgumentNullException(nameof(graph));
        _kindRegistry         = kindRegistry;
        _channelCommands      = channelCommands;
        _peerSignatureLookup  = peerSignatureLookup;
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
    /// Rebuilds the node/pin/link projection from the current asset state.
    /// Uses a <b>two-pass GUID-binding algorithm</b> so that connected pins
    /// receive the GUIDs carried in the asset's <c>Link.FromPinId</c> /
    /// <c>Link.ToPinId</c> fields, making wires resolve even when the asset
    /// stores <c>"Pins": []</c>.
    ///
    /// <para><b>Pass 1 — pin GUID resolution per node (link-GUID-driven slow path):</b></para>
    /// <list type="number">
    ///   <item>Get the canonical pin list via <see cref="NodePinSchema.GetCanonicalPins"/>.</item>
    ///   <item>Collect all links incident on this node.</item>
    ///   <item>
    ///     Collect the distinct <c>FromPinId</c> GUIDs from outgoing links and distinct
    ///     <c>ToPinId</c> GUIDs from incoming links (deduplication handles fan-out).
    ///     Assign each distinct outgoing GUID to the next output pin in declaration order;
    ///     same for incoming GUIDs → input pins.
    ///     Unconnected pins receive <c>IdGenerator.Deterministic("pin:{nodeId}:{name}:{dir}")</c>.
    ///     Invariant: every link endpoint GUID maps to a pin, so wires always resolve.
    ///   </item>
    /// </list>
    ///
    /// <para><b>Pass 2 — build models:</b></para>
    /// Build <see cref="BlueprintNodeModel"/> instances with the resolved pin list,
    /// then build <see cref="BlueprintLinkModel"/> instances (they now resolve via
    /// the pin dictionary).
    /// </summary>
    public void Rebuild()
    {
        var nodes = new Dictionary<NodeId, INodeModel>();
        var pins  = new Dictionary<PinId,  IPinModel>();
        var links = new Dictionary<LinkId, ILinkModel>();

        // ── Pass 1: resolve per-node pin GUIDs ──────────────────────────────

        // Build a node-id → incident links lookup once for efficiency.
        var linksFromNode = new Dictionary<Guid, List<Link>>();
        var linksToNode   = new Dictionary<Guid, List<Link>>();
        foreach (var assetLink in _graph.Links)
        {
            if (!linksFromNode.TryGetValue(assetLink.FromNodeId, out var fromList))
                linksFromNode[assetLink.FromNodeId] = fromList = new();
            fromList.Add(assetLink);

            if (!linksToNode.TryGetValue(assetLink.ToNodeId, out var toList))
                linksToNode[assetLink.ToNodeId] = toList = new();
            toList.Add(assetLink);
        }

        // For each asset node, resolve a pin list with correct GUIDs.
        var resolvedPinLists = new Dictionary<Guid, List<IPinModel>>();

        foreach (var assetNode in _graph.Nodes)
        {
            var canonicalPins = NodePinSchema.GetCanonicalPins(assetNode, _kindRegistry, _asset, _channelCommands, _graph, _peerSignatureLookup);

            // Separate incident links by direction.
            linksFromNode.TryGetValue(assetNode.Id, out var outLinks);
            linksToNode.TryGetValue(assetNode.Id,   out var inLinks);

            outLinks ??= _emptyLinks;
            inLinks  ??= _emptyLinks;

            // Assign GUIDs to pins.
            // FAST PATH: if the asset node already had pins (builder-created or freshly-added
            // nodes), their GUIDs are authoritative — use them directly without rebinding.
            // This preserves the original GUIDs so AddLink validation works correctly.
            var assetHadPins = assetNode.Pins.Count > 0;
            var pinGuidMap   = new Dictionary<Pin, Guid>(ReferenceEqualityComparer.Instance);

            if (assetHadPins)
            {
                // Canonical pins == node.Pins, already have the correct GUIDs.
                foreach (var pin in canonicalPins)
                    pinGuidMap[pin] = pin.Id;
            }
            else
            {
                // SLOW PATH: JSON-loaded assets (Pins: []).
                // LINK-GUID-DRIVEN: driven by the distinct link pin GUIDs, not by pin index.
                //
                // 1. Collect the distinct FromPinId GUIDs from all outgoing links and
                //    the distinct ToPinId GUIDs from all incoming links.  (Fan-out shares
                //    one FromPinId so deduplication is critical.)
                // 2. Assign each distinct outgoing GUID to the next available output pin
                //    (in pin declaration order).  Same for incoming GUIDs → input pins.
                // 3. Any pin with no link GUID assigned gets a deterministic synthetic GUID.
                //
                // Invariant: every link endpoint GUID is assigned to exactly one pin of the
                // matching direction, so FindPin succeeds for every link in the graph.

                var outPins = canonicalPins.Where(p => p.Direction == "Out").ToList();
                var inPins  = canonicalPins.Where(p => p.Direction == "In").ToList();

                // Collect distinct link-endpoint GUIDs in order of first occurrence.
                var distinctOutGuids = new List<Guid>();
                var seenOut = new HashSet<Guid>();
                foreach (var link in outLinks)
                    if (seenOut.Add(link.FromPinId))
                        distinctOutGuids.Add(link.FromPinId);

                var distinctInGuids = new List<Guid>();
                var seenIn = new HashSet<Guid>();
                foreach (var link in inLinks)
                    if (seenIn.Add(link.ToPinId))
                        distinctInGuids.Add(link.ToPinId);

                // Assign output pins: first N pins get the N distinct link GUIDs; rest get deterministic.
                for (int i = 0; i < outPins.Count; i++)
                {
                    var pin  = outPins[i];
                    var guid = (i < distinctOutGuids.Count)
                        ? distinctOutGuids[i]
                        : IdGenerator.Deterministic($"pin:{assetNode.Id:N}:{pin.Name}:Out");
                    pinGuidMap[pin] = guid;
                }

                // Assign input pins: first N pins get the N distinct link GUIDs; rest get deterministic.
                for (int i = 0; i < inPins.Count; i++)
                {
                    var pin  = inPins[i];
                    var guid = (i < distinctInGuids.Count)
                        ? distinctInGuids[i]
                        : IdGenerator.Deterministic($"pin:{assetNode.Id:N}:{pin.Name}:In");
                    pinGuidMap[pin] = guid;
                }
            }

            // Build the resolved IPinModel list.
            var nodeId = new NodeId(assetNode.Id);
            var resolvedPins = new List<IPinModel>(canonicalPins.Count);
            foreach (var pin in canonicalPins)
            {
                var resolvedGuid = pinGuidMap.TryGetValue(pin, out var g) ? g : Guid.NewGuid();
                // Construct a synthetic Pin with the resolved GUID so BlueprintPinModel works.
                // Carry over DefaultValue from node.PinDefaults (persisted on the asset) so the
                // canvas inline editor reads the previously-set value after reload.
                string? defaultVal = null;
                assetNode.PinDefaults?.TryGetValue(pin.Name, out defaultVal);
                var resolvedPin = new Hrot.Blueprints.Core.Assets.Pin
                {
                    Id           = resolvedGuid,
                    Name         = pin.Name,
                    Direction    = pin.Direction,
                    IsExec       = pin.IsExec,
                    TypeRef      = pin.TypeRef,
                    DefaultValue = defaultVal,
                };
                resolvedPins.Add(new BlueprintPinModel(resolvedPin, nodeId));
            }
            resolvedPinLists[assetNode.Id] = resolvedPins;
        }

        // ── Pass 2: build node and link models ───────────────────────────────

        foreach (var assetNode in _graph.Nodes)
        {
            var resolvedPins = resolvedPinLists[assetNode.Id];
            var nodeModel    = new BlueprintNodeModel(assetNode, resolvedPins, _asset);
            nodes[nodeModel.Id] = nodeModel;
            foreach (var pin in resolvedPins)
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

    private static readonly List<Link> _emptyLinks = new();

    /// <summary>Fires <see cref="Changed"/> with <see cref="GraphChangeKind.Wholesale"/>.</summary>
    public void NotifyChanged()
        => Changed?.Invoke(new GraphChangeNotification(GraphChangeKind.Wholesale, null, null, null, null));

    /// <summary>
    /// Fires <see cref="Changed"/> with <see cref="GraphChangeKind.NodesMoved"/>
    /// for the given node IDs.  Does NOT rebuild the projection caches, so the
    /// existing <see cref="INodeModel"/> instances are preserved (identity stable).
    /// Call after mutating <see cref="BlueprintNodeModel.SetPosition"/> in place.
    /// </summary>
    public void NotifyMoved(IReadOnlyCollection<NodeId> movedIds)
        => Changed?.Invoke(new GraphChangeNotification(
            GraphChangeKind.NodesMoved,
            new HashSet<NodeId>(movedIds),
            null, null, null));

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
