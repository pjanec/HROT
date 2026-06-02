using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <para>
/// <see cref="INodeCatalog"/> that wraps the static <see cref="NodeKindRegistry"/> palette
/// (node-drawer descriptors) and supplements it with dynamic entries derived from
/// a <see cref="BlueprintAsset"/>'s <c>CallablePeers</c> and <c>CustomEvents</c>.
/// </para>
/// <para>
/// Call <see cref="Refresh"/> (or set <see cref="Asset"/>) to rebuild the dynamic
/// entries when the asset changes; <see cref="CatalogChanged"/> fires on every rebuild.
/// </para>
/// </summary>
public sealed class BlueprintNodeCatalog : INodeCatalog
{
    private readonly NodeKindRegistry _registry;
    private BlueprintAsset?           _asset;
    private List<NodeCatalogEntry>    _all = new();

    // ── categories ────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<NodeCategoryDescriptor> _categories = new List<NodeCategoryDescriptor>
    {
        new("Events",       "Events",        null),
        new("Flow Control", "Flow Control",  null),
        new("Variables",    "Variables",     null),
        new("Math",         "Math",          null),
        new("Logic",        "Logic",         null),
        new("Utility",      "Utility",       null),
        new("EQS",          "EQS",           null),
        new("Reactive",     "Reactive",      null),
        new("Peers",        "Callable Peers",null),
        new("CustomEvents", "Custom Events", null),
    };

    // ── ctor ──────────────────────────────────────────────────────────────────

    /// <param name="registry">
    /// The static node-kind palette registered by the Blueprint editor at startup.
    /// </param>
    public BlueprintNodeCatalog(NodeKindRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        BuildAll();
    }

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// The underlying <see cref="NodeKindRegistry"/> used to resolve
    /// <see cref="NodeEditor.Primitives.NodeKindKey"/> to asset-node factory descriptors.
    /// Exposed so <see cref="BlueprintCommandSink"/> can create correctly-typed asset nodes.
    /// </summary>
    public NodeKindRegistry KindRegistry => _registry;

    /// <summary>Fired whenever the catalog entries change (e.g. after <see cref="Refresh"/>).</summary>
    public event Action? CatalogChanged;

    /// <summary>
    /// Gets or sets the asset whose <c>CallablePeers</c> and <c>CustomEvents</c> are projected
    /// as dynamic catalog entries.  Setting this property triggers a <see cref="Refresh"/>.
    /// </summary>
    public BlueprintAsset? Asset
    {
        get => _asset;
        set
        {
            _asset = value;
            Refresh();
        }
    }

    /// <summary>Rebuilds the catalog from the current registry and asset, then fires <see cref="CatalogChanged"/>.</summary>
    public void Refresh()
    {
        BuildAll();
        CatalogChanged?.Invoke();
    }

    // ── INodeCatalog ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<NodeCatalogEntry> All => _all;

    /// <inheritdoc/>
    public IReadOnlyList<NodeCategoryDescriptor> Categories => _categories;

    /// <inheritdoc/>
    public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q)
    {
        var text = q.Text;
        return _all.Where(e =>
            (text.Length == 0
                || e.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || e.Kind.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (e.CategoryPath?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Keywords.Any(k => k.Contains(text, StringComparison.OrdinalIgnoreCase)))
            && (q.CategoryFilter is null
                || (e.CategoryPath?.StartsWith(q.CategoryFilter, StringComparison.OrdinalIgnoreCase) ?? false))
            && (!q.IncludeDeprecated ? !e.IsDeprecated : true)
        ).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q)
    {
        var baseResults = Query(new NodeSearchQuery(q.Text));
        var targetDir = q.SourceDirection == PinDirection.Output
            ? PinDirection.Input
            : PinDirection.Output;

        return baseResults.Where(entry =>
        {
            var targetPins = targetDir == PinDirection.Input ? entry.Inputs : entry.Outputs;
            return targetPins.Any(p =>
                p.Kind == q.SourceKind &&
                (q.SourceKind == PinKind.Exec || p.Type == q.SourceType));
        }).ToList();
    }

    // ── build ─────────────────────────────────────────────────────────────────

    private void BuildAll()
    {
        var entries = new List<NodeCatalogEntry>();

        // ── static entries from NodeKindRegistry ─────────────────────────────
        foreach (var descriptor in _registry.EnumerateAll())
        {
            entries.Add(DescriptorToEntry(descriptor));
        }

        // ── dynamic entries from asset ────────────────────────────────────────
        if (_asset is not null)
        {
            // Custom events → Call{EventName} entries.
            foreach (var ce in _asset.CustomEvents)
            {
                entries.Add(MakeCustomEventEntry(ce));
            }

            // Callable peers → CallPeer{PeerId} entries.
            foreach (var peerId in _asset.CallablePeers)
            {
                entries.Add(MakeCallablePeerEntry(peerId));
            }
        }

        _all = entries;
    }

    // ── conversion helpers ────────────────────────────────────────────────────

    private static NodeCatalogEntry DescriptorToEntry(NodeKindDescriptor d)
    {
        // Build pin signatures from a default-constructed node.
        Node defaultNode;
        try
        {
            defaultNode = d.CreateInstance();
        }
        catch
        {
            defaultNode = null!;
        }

        IReadOnlyList<PinSignature> inputs  = Array.Empty<PinSignature>();
        IReadOnlyList<PinSignature> outputs = Array.Empty<PinSignature>();

        if (defaultNode is not null)
        {
            inputs  = defaultNode.Pins
                .Where(p => p.Direction == "In")
                .Select(PinToSignature)
                .ToList();
            outputs = defaultNode.Pins
                .Where(p => p.Direction == "Out")
                .Select(PinToSignature)
                .ToList();
        }

        return new NodeCatalogEntry(
            Kind:         new NodeKindKey(d.Kind),
            DisplayName:  d.DisplayName,
            Description:  string.IsNullOrEmpty(d.Tooltip) ? null : d.Tooltip,
            CategoryPath: string.IsNullOrEmpty(d.Category) ? null : d.Category,
            Keywords:     Array.Empty<string>(),
            IconKey:      string.IsNullOrEmpty(d.Icon) ? null : d.Icon,
            IsPure:       false,
            IsLatent:     false,
            IsDeprecated: false,
            Inputs:       inputs,
            Outputs:      outputs);
    }

    private static PinSignature PinToSignature(Pin pin)
    {
        var kind = pin.IsExec ? PinKind.Exec : PinKind.Data;
        TypeKey? type = pin.IsExec
            ? null
            : string.IsNullOrEmpty(pin.TypeRef.TypeId)
                ? null
                : new TypeKey(pin.TypeRef.TypeId);
        return new PinSignature(pin.Name, kind, type, false);
    }

    private static NodeCatalogEntry MakeCustomEventEntry(CustomEventDecl ce)
    {
        var inputs = new List<PinSignature>
        {
            new("In", PinKind.Exec, null, false),
        };
        var outputs = new List<PinSignature>
        {
            new("Out", PinKind.Exec, null, false),
        };
        foreach (var param in ce.Parameters)
        {
            inputs.Add(new PinSignature(
                param.Name,
                PinKind.Data,
                string.IsNullOrEmpty(param.Type.TypeId) ? null : new TypeKey(param.Type.TypeId),
                false));
        }

        return new NodeCatalogEntry(
            Kind:         new NodeKindKey($"CustomEvent.{ce.Name}"),
            DisplayName:  $"Call {ce.Name}",
            Description:  $"Call custom event '{ce.Name}'",
            CategoryPath: "CustomEvents",
            Keywords:     new[] { ce.Name },
            IconKey:      null,
            IsPure:       false,
            IsLatent:     false,
            IsDeprecated: false,
            Inputs:       inputs,
            Outputs:      outputs);
    }

    private static NodeCatalogEntry MakeCallablePeerEntry(Guid peerId)
    {
        var peerIdStr = peerId.ToString("N");
        return new NodeCatalogEntry(
            Kind:         new NodeKindKey($"CallPeer.{peerIdStr}"),
            DisplayName:  $"Call Peer ({peerIdStr[..8]}…)",
            Description:  $"Call a function on peer Blueprint {peerId}",
            CategoryPath: "Peers",
            Keywords:     new[] { peerIdStr },
            IconKey:      null,
            IsPure:       false,
            IsLatent:     false,
            IsDeprecated: false,
            Inputs:       new[] { new PinSignature("In",  PinKind.Exec, null, false) },
            Outputs:      new[] { new PinSignature("Out", PinKind.Exec, null, false) });
    }
}
