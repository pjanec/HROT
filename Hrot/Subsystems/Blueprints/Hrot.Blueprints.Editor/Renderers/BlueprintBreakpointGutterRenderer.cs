using System.Numerics;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Editor.Renderers;

/// <summary>
/// Renders a small red filled circle in the node gutter for each node
/// that has an active breakpoint set on it in the current debug session.
/// Mirrors <c>BTreeBreakpointGutterRenderer</c> but uses
/// <see cref="IBlueprintDebugSession"/> instead of the BTree session + manager.
/// </summary>
public sealed class BlueprintBreakpointGutterRenderer : ICustomCanvasRenderer
{
    private readonly BlueprintAsset _asset;
    private IBlueprintDebugSession? _session;

    public string Id => "blueprint.breakpoint_gutter";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public BlueprintBreakpointGutterRenderer(BlueprintAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public void SetSession(IBlueprintDebugSession? session) => _session = session;

    /// <summary>
    /// True when a session is attached (renderer has work to do).
    /// When false the canvas skips the render pass entirely.
    /// </summary>
    public bool IsActive => _session != null;

    public void Render(ICanvasRenderContext ctx)
    {
        if (_session is null) return;

        var breakpoints = _session.GetBreakpoints();
        foreach (var bp in breakpoints)
        {
            if (!bp.Enabled) continue;
            if (bp.AssetId != _asset.AssetId) continue;

            var node = FindNode(bp.NodeId);
            if (node is null) continue;

            var nodePos = new Vector2(node.EditorMetadata.X, node.EditorMetadata.Y);
            var screenPos = ctx.Viewport.GraphToScreen(nodePos);
            var center = screenPos + new Vector2(-8f, 8f) * ctx.Zoom;
            float radius = 5f * ctx.Zoom;
            var color = new Vector4(0.9f, 0.15f, 0.15f, 1.0f);

            ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(color));
        }
    }

    private Node? FindNode(string nodeId)
    {
        // Search all graphs in the asset for the node matching the bp.NodeId.
        // node.Id is Guid; bp.NodeId is string (Guid "D" format).
        if (!Guid.TryParse(nodeId, out var nodeGuid))
            return null;
        foreach (var graph in _asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                if (node.Id == nodeGuid)
                    return node;
            }
        }
        return null;
    }
}
