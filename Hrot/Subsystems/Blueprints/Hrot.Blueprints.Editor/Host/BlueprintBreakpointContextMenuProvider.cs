using Hrot.Blueprints.Core.Debug;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Provides right-click context menu items for Blueprint node breakpoints.
/// Matched by RendererId to <see cref="Renderers.BlueprintBreakpointGutterRenderer.Id"/>.
/// Calls <see cref="IBlueprintDebugSession.SetBreakpoint"/>/<see cref="IBlueprintDebugSession.ClearBreakpoint"/>
/// — dual-registration with the data-breakpoint manager is automatic (Q1 resolved).
/// </summary>
internal sealed class BlueprintBreakpointContextMenuProvider : ICustomElementContextMenuProvider
{
    private readonly IBlueprintDebugSession _session;
    private readonly Guid _assetId;
    private readonly Guid _graphId;

    public BlueprintBreakpointContextMenuProvider(
        IBlueprintDebugSession session,
        Guid assetId,
        Guid graphId)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _assetId = assetId;
        _graphId = graphId;
    }

    public string RendererId => "blueprint.breakpoint_gutter";

    public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
    {
        // elementKey encodes the node id string (Guid "D" format).
        if (!Guid.TryParse(elementKey, out var nodeId))
            return Array.Empty<ContextMenuItem>();

        // Check if there's already a breakpoint on this node in this asset+graph
        var existing = _session.GetBreakpoints()
            .FirstOrDefault(bp => bp.AssetId == _assetId
                               && bp.GraphId == _graphId
                               && bp.NodeId == nodeId.ToString("D"));

        var items = new List<ContextMenuItem>();
        if (existing != null)
        {
            var bpId = existing.Id;
            items.Add(new ContextMenuItem(
                "Clear Breakpoint",
                () => _session.ClearBreakpoint(bpId),
                Enabled: true));
        }
        else
        {
            items.Add(new ContextMenuItem(
                "Toggle Breakpoint",
                () => _session.SetBreakpoint(_assetId, _graphId, nodeId),
                Enabled: true));
        }
        return items;
    }
}
