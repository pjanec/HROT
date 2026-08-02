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
    private readonly IPinDefaultValueEditorRegistry? _editorRegistry;
    // ENUM-NAME: provider used to resolve persisted member-name strings back to long for the editor.
    private readonly IEnumValueProvider? _enumProvider;
    // AN7: unified behavior-action catalog so non-channel ChannelCommandNodes (ActionFqn set)
    // project their parameter data-IN pins. Mirrors how _channelCommands is threaded.
    private readonly ActionCatalog.IBehaviorActionCatalog? _behaviorActions;

    // Projection caches (rebuilt when the asset graph mutates).
    private Dictionary<NodeId,    INodeModel>    _nodes    = new();
    private Dictionary<LinkId,    ILinkModel>    _links    = new();
    private Dictionary<PinId,     IPinModel>     _pins     = new();
    private Dictionary<CommentId, ICommentModel> _comments = new();

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
    /// <param name="editorRegistry">
    /// Optional pin-default-value editor registry.  When non-null, unconnected In-data pins
    /// whose type has a registered editor expose a non-null <see cref="IPinModel.Default"/> even
    /// before a value has been set, so the inline widget renders at zero on first use.
    /// When null (default), the legacy behavior applies: <c>Default</c> is only non-null when
    /// a value has already been persisted in <see cref="Node.PinDefaults"/>.
    /// </param>
    /// <param name="enumProvider">
    /// Optional enum-value provider forwarded to <see cref="BlueprintPinModel"/> so that
    /// enum defaults persisted as member name strings (ENUM-NAME) are resolved to the correct
    /// <c>long</c> when the canvas inline editor first reads the pin default.
    /// </param>
    /// <param name="behaviorActions">
    /// AN7 — optional unified behavior-action catalog forwarded to
    /// <see cref="NodePinSchema.GetCanonicalPins"/> so a non-channel <see cref="ChannelCommandNode"/>
    /// (one whose <c>ActionFqn</c> is set) projects its parameter data-IN pins from the matching
    /// catalog entry's params type. When <see langword="null"/> such nodes fall back to exec-only.
    /// </param>
    public BlueprintGraphModel(
        BlueprintAsset    asset,
        Graph             graph,
        NodeKindRegistry? kindRegistry = null,
        IChannelCommandCatalog? channelCommands = null,
        Func<Guid, BlueprintSignature?>? peerSignatureLookup = null,
        IPinDefaultValueEditorRegistry? editorRegistry = null,
        IEnumValueProvider? enumProvider = null,
        ActionCatalog.IBehaviorActionCatalog? behaviorActions = null)
    {
        _asset                = asset ?? throw new ArgumentNullException(nameof(asset));
        _graph                = graph ?? throw new ArgumentNullException(nameof(graph));
        _kindRegistry         = kindRegistry;
        _channelCommands      = channelCommands;
        _peerSignatureLookup  = peerSignatureLookup;
        _editorRegistry       = editorRegistry;
        _enumProvider         = enumProvider;
        _behaviorActions      = behaviorActions;
        Rebuild();
    }

    // ── IGraphModel ──────────────────────────────────────────────────────────

    public GraphId Id          => new(IdGenerator.Deterministic($"graph:{_asset.AssetId}:{_graph.Id}"));
    public string  DisplayName => _graph.Name;
    public GraphKindDescriptor Kind { get; } =
        new("EventGraph", "Event Graph", AllowsLatent: true, RequiresEntryNode: true);

    public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
    public IReadOnlyCollection<ILinkModel>    Links    => _links.Values;
    public IReadOnlyCollection<ICommentModel> Comments => _comments.Values;

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel?    FindNode(NodeId id)       => _nodes.TryGetValue(id,    out var v) ? v : null;
    public IPinModel?     FindPin(PinId id)         => _pins.TryGetValue(id,     out var v) ? v : null;
    public ILinkModel?    FindLink(LinkId id)       => _links.TryGetValue(id,    out var v) ? v : null;
    public ICommentModel? FindComment(CommentId id) => _comments.TryGetValue(id, out var v) ? v : null;

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
        var nodes    = new Dictionary<NodeId, INodeModel>();
        var pins     = new Dictionary<PinId,  IPinModel>();
        var links    = new Dictionary<LinkId, ILinkModel>();
        var comments = new Dictionary<CommentId, ICommentModel>();

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
            var canonicalPins = NodePinSchema.GetCanonicalPins(assetNode, _kindRegistry, _asset, _channelCommands, _graph, _peerSignatureLookup, _behaviorActions);

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
                // Per-pin blend of the deterministic content-derived scheme (architect Q#10-A/C) and the
                // legacy positional scheme, in strict parity with the compiler's
                // Stage0_Rehydrate.AssignLinkGuids/AssignDirection:
                //   * A link whose pin-GUID equals a pin's deterministic GUID binds THAT pin by name
                //     (order-independent — the exec/data swap the old positional scheme suffered is gone).
                //   * Remaining links (legacy GUIDs) bind positionally to the still-unassigned pins;
                //     leftover unconnected pins get the deterministic synthetic GUID.
                // Handles migrated, legacy, and mixed nodes (a link drawn to a pin that had been saved
                // unconnected already carries that pin's deterministic GUID) so every link resolves.
                AssignDirectionEditor(canonicalPins.Where(p => p.Direction == "Out").ToList(),
                    assetNode.Id, "Out", DistinctPinGuids(outLinks, l => l.FromPinId), pinGuidMap);
                AssignDirectionEditor(canonicalPins.Where(p => p.Direction == "In").ToList(),
                    assetNode.Id, "In", DistinctPinGuids(inLinks, l => l.ToPinId), pinGuidMap);
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
                bool literalInlinePin = assetNode is Hrot.Blueprints.Core.Assets.LiteralNode
                    && pin.Direction == "In" && !pin.IsExec;
                if (literalInlinePin)
                {
                    // Literal's editor-only input pin: seed the inline editor from ValueJson (not PinDefaults).
                    var litForPin = (Hrot.Blueprints.Core.Assets.LiteralNode)assetNode;
                    defaultVal = LiteralValueJson.ToEditString(litForPin.TypeId, litForPin.ValueJson);
                }
                else
                {
                    assetNode.PinDefaults?.TryGetValue(pin.Name, out defaultVal);
                }
                var resolvedPin = new Hrot.Blueprints.Core.Assets.Pin
                {
                    Id           = resolvedGuid,
                    Name         = pin.Name,
                    Direction    = pin.Direction,
                    IsExec       = pin.IsExec,
                    TypeRef      = pin.TypeRef,
                    DefaultValue = defaultVal,
                };
                var displayLabel = ResolvePinDisplayLabel(assetNode, resolvedPin);
                resolvedPins.Add(new BlueprintPinModel(resolvedPin, nodeId, _editorRegistry, _enumProvider, displayLabel, glyphless: literalInlinePin));
            }
            resolvedPinLists[assetNode.Id] = resolvedPins;
        }

        // ── Pass 2: build node and link models ───────────────────────────────

        foreach (var assetNode in _graph.Nodes)
        {
            var resolvedPins = resolvedPinLists[assetNode.Id];

            // CA-07c/CA-07d-1: the five collection CONSUMER kinds need to know whether their "Collection"
            // data-IN pin is CURRENTLY wired (BlueprintNodeModel's BP2066-mirroring stale-bake error
            // check) -- this constructor has no other connectivity signal, so resolve it here where
            // _graph.Links is in scope.
            var collectionPinWired = assetNode is ComponentForEachNode or ComponentItemGetNode or ComponentItemCountNode or ComponentContainsNode or ComponentFindNode
                && resolvedPins.Any(p => p.Direction == PinDirection.Input
                                       && p.Label == "Collection"
                                       && _graph.Links.Any(l => l.ToNodeId == assetNode.Id && l.ToPinId == p.Id.Value));

            var nodeModel = new BlueprintNodeModel(assetNode, resolvedPins, _asset, collectionPinWired);
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
            links[linkId] = new BlueprintLinkModel(linkId, fromPin, toPin, assetLink.Waypoints);
        }

        // ── Comments: pure editor annotations, no pin/node resolution needed ──
        foreach (var assetComment in _graph.Comments)
        {
            var commentModel = new BlueprintCommentModel(assetComment);
            comments[commentModel.Id] = commentModel;
        }

        _nodes    = nodes;
        _pins     = pins;
        _links    = links;
        _comments = comments;
    }

    /// <summary>
    /// Render-only display label for a projected data pin, when it reads clearer than the pin's
    /// identity Name. GetParameter's generic "Value" out-pin is relabeled with the referenced
    /// parameter's NAME (the node title stays clean). The pin's identity Name is untouched, so
    /// GUIDs / link rehydration are unaffected. Returns null to keep the pin's Name.
    /// (Get/SetShared + Get/SetVariable can adopt the same treatment here once confirmed.)
    /// </summary>
    private string? ResolvePinDisplayLabel(
        Hrot.Blueprints.Core.Assets.Node node,
        Hrot.Blueprints.Core.Assets.Pin pin)
    {
        if (pin.IsExec || pin.Name != "Value") return null;

        return node switch
        {
            Hrot.Blueprints.Core.Assets.GetParameterNode gp => ResolveParameterLabel(gp.ParameterId),
            // Get/SetShared: VariableId is already the shared field's slot name — show it on the pin.
            Hrot.Blueprints.Core.Assets.GetSharedNode gsn => string.IsNullOrEmpty(gsn.VariableId) ? null : gsn.VariableId,
            Hrot.Blueprints.Core.Assets.SetSharedNode ssn => string.IsNullOrEmpty(ssn.VariableId) ? null : ssn.VariableId,
            _ => null,
        };
    }

    private string? ResolveParameterLabel(string parameterId)
    {
        if (_asset == null || string.IsNullOrEmpty(parameterId)) return null;

        var id = parameterId;
        if (id.StartsWith("param:", System.StringComparison.OrdinalIgnoreCase)) id = id[6..];
        else if (id.StartsWith("var:", System.StringComparison.OrdinalIgnoreCase)) id = id[4..];

        if (System.Guid.TryParse(id, out var guid))
        {
            var decl = _asset.Parameters.FirstOrDefault(p => p.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Name)) return decl.Name;
        }
        return null;
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

    /// <summary>Distinct incident-link pin GUIDs in first-occurrence order (fan-out shares one GUID).</summary>
    private static List<Guid> DistinctPinGuids(IEnumerable<Link> links, Func<Link, Guid> select)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var link in links)
        {
            var g = select(link);
            if (seen.Add(g)) result.Add(g);
        }
        return result;
    }

    /// <summary>Per-direction pin-GUID binding shared by the slow path (parity with the compiler's
    /// <c>Stage0_Rehydrate.AssignDirection</c>): deterministic-GUID links bind their pin by name, remaining
    /// legacy links bind positionally to the unassigned pins, leftover pins get a deterministic GUID.</summary>
    private static void AssignDirectionEditor(
        List<Pin> dirPins, Guid nodeId, string direction, List<Guid> linkGuids,
        Dictionary<Pin, Guid> pinGuidMap)
    {
        var detToPin = new Dictionary<Guid, Pin>();
        foreach (var pin in dirPins)
            detToPin[IdGenerator.Deterministic($"pin:{nodeId:N}:{pin.Name}:{direction}")] = pin;

        var assigned = new HashSet<Pin>(ReferenceEqualityComparer.Instance);
        var legacyGuids = new List<Guid>();
        foreach (var g in linkGuids)
        {
            if (detToPin.TryGetValue(g, out var pin))
            {
                if (assigned.Add(pin)) pinGuidMap[pin] = g;
            }
            else
            {
                legacyGuids.Add(g);
            }
        }

        int li = 0;
        foreach (var pin in dirPins)
        {
            if (assigned.Contains(pin)) continue;
            pinGuidMap[pin] = (li < legacyGuids.Count)
                ? legacyGuids[li++]
                : IdGenerator.Deterministic($"pin:{nodeId:N}:{pin.Name}:{direction}");
        }
    }

    /// <summary>
    /// Derives a deterministic <see cref="LinkId"/> from the two pin guids.
    /// Same (from, to) pair always yields the same id.
    /// </summary>
    public static LinkId MakeLinkId(Guid fromPinId, Guid toPinId)
    {
        var key = $"link:{fromPinId:N}:{toPinId:N}";
        return new LinkId(IdGenerator.Deterministic(key));
    }

    /// <summary>
    /// Finds the asset-level <see cref="Link"/> whose derived <see cref="LinkId"/> equals
    /// <paramref name="id"/>.  Uses the canonical <see cref="MakeLinkId"/> derivation so the
    /// resolution is consistent with the projection.
    /// Returns <see langword="null"/> when no matching link exists (safe no-op for callers).
    /// </summary>
    internal Link? FindAssetLink(LinkId id)
    {
        foreach (var link in _graph.Links)
        {
            if (MakeLinkId(link.FromPinId, link.ToPinId) == id)
                return link;
        }
        return null;
    }
}
