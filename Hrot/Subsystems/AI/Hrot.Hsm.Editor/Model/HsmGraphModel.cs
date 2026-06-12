using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Model;

// NodeEditor IGraphModel adapter for HsmAsset.
// Exposes all non-root StateNodes as INodeModel instances
// and all transitions as ILinkModel instances.
// This is the read-only view; mutations go through HsmCommandSink.
public sealed class HsmGraphModel : IGraphModel
{
    private readonly HsmAsset _asset;

    // Cache for link adapters keyed by VisualId.
    private readonly Dictionary<LinkId, HsmTransitionLink> _linkCache = new();

    public HsmGraphModel(HsmAsset asset)
    {
        _asset = asset;
        // Rebuild caches when asset changes.
        _asset.Changed += OnAssetChanged;
        BuildCaches();
    }

    private void OnAssetChanged()
    {
        BuildCaches();
        Changed?.Invoke(new GraphChangeNotification(
            GraphChangeKind.NodesModified,
            null, null, null, "HsmAsset changed"));
    }

    private void BuildCaches()
    {
        _linkCache.Clear();

        // Run validation once (include blackboard for region-conflict checks).
        var diagnostics = new HsmValidator().Validate(_asset, _asset as IBlackboardManagedAsset);

        // Map StableId -> worst (Error-wins) severity + message.
        var perState = new Dictionary<Guid, (NodeState State, string Tooltip)>();
        foreach (var d in diagnostics)
        {
            NodeState sev = d.Severity switch
            {
                HsmDiagnosticSeverity.Error   => NodeState.Error,
                HsmDiagnosticSeverity.Warning => NodeState.Warning,
                _                             => NodeState.Normal,
            };
            if (sev == NodeState.Normal) continue;
            foreach (var id in d.TargetStableIds)
            {
                if (!perState.TryGetValue(id, out var ex))
                    perState[id] = (sev, d.Message);
                else if (sev == NodeState.Error && ex.State != NodeState.Error)
                    perState[id] = (sev, d.Message);
            }
        }

        // Project onto states; RESET to null when no diagnostic (preserves breakpoint state).
        foreach (var s in _asset.AllStates)
        {
            if (perState.TryGetValue(s.StableId, out var diag))
            { s.DiagnosticState = diag.State; s.DiagnosticTooltip = diag.Tooltip; }
            else
            { s.DiagnosticState = null; s.DiagnosticTooltip = null; }
        }

        foreach (var t in _asset.AllTransitions)
            _linkCache[new LinkId(t.VisualId)] = new HsmTransitionLink(t);

        LastDiagnostics = diagnostics;
        DiagnosticsRecomputed?.Invoke(diagnostics);
    }

    public IReadOnlyList<HsmDiagnostic> LastDiagnostics { get; private set; } = Array.Empty<HsmDiagnostic>();
    public event Action<IReadOnlyList<HsmDiagnostic>>? DiagnosticsRecomputed;

    // ---- IGraphModel ----

    public GraphId Id          => new GraphId(_asset.AssetId);
    public string  DisplayName => _asset.Name;
    public GraphKindDescriptor Kind { get; } =
        new("HsmGraph", "State Machine", AllowsLatent: false, RequiresEntryNode: false);

    // Nodes: all non-root states (RootState is synthetic and never shown).
    public IReadOnlyCollection<INodeModel> Nodes => _asset.AllStates;

    public IReadOnlyCollection<ILinkModel> Links => _linkCache.Values;

    public IReadOnlyCollection<ICommentModel> Comments =>
        Array.Empty<ICommentModel>();

    public event Action<GraphChangeNotification>? Changed;

    public INodeModel? FindNode(NodeId id)
    {
        var state = _asset.FindStateByStableId(id.Value);
        return state;
    }

    public IPinModel? FindPin(PinId id)
    {
        // Search all states' pins (two per state: output then input).
        foreach (var state in _asset.AllStates)
        {
            foreach (var pin in state.Pins)
                if (pin.Id == id) return pin;
        }
        return null;
    }

    public ILinkModel? FindLink(LinkId id) =>
        _linkCache.TryGetValue(id, out var link) ? link : null;

    public IReadOnlyCollection<IAttachmentModel> Attachments =>
        _asset.AllAttachments.ToList<IAttachmentModel>();

    public IAttachmentModel? FindAttachment(AttachmentId id) =>
        _asset.FindAttachmentById(id);

    public IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId) =>
        _asset.GetAttachmentsForNode(hostId).ToList<IAttachmentModel>();
}
