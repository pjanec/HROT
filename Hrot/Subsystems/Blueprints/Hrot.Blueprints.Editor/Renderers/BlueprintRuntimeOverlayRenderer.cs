using System.Numerics;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Editor.Renderers;

/// <summary>
/// Custom canvas renderer that draws a runtime overlay when a Blueprint debug session
/// is active. Mirrors <c>BTreeRuntimeOverlayRenderer</c>.
/// Renders:
///   - Pulsing gold outline on the currently executing node (from recent history).
///   - Red outline on the paused-at node (when session is paused on a breakpoint).
///   - Dim dots on recently executed nodes (history trail).
/// Registered at the AfterNodes pass (after the gutter renderer) so overlays appear
/// above node bodies.
/// </summary>
public sealed class BlueprintRuntimeOverlayRenderer : ICustomCanvasRenderer
{
    private static readonly Vector4 RunningColor = new(1.00f, 0.85f, 0.00f, 0.90f);  // gold
    private static readonly Vector4 PausedColor  = new(0.90f, 0.15f, 0.15f, 0.90f);  // red
    private static readonly Vector4 HistoryColor = new(0.50f, 0.50f, 0.80f, 0.60f);  // dim blue
    private static readonly Vector2  DefaultNodeSize = new(120f, 48f);

    private readonly BlueprintAsset _asset;
    private IBlueprintDebugSession? _session;

    public string Id => "blueprint.runtime_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public BlueprintRuntimeOverlayRenderer(BlueprintAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    /// <summary>
    /// False when no debug session is attached — the canvas skips the renderer
    /// entirely so there is no per-frame overhead during authoring.
    /// </summary>
    public bool IsActive => _session != null;

    /// <summary>Attaches or detaches the debug session used for overlay data.</summary>
    public void SetSession(IBlueprintDebugSession? session) => _session = session;

    /// <summary>
    /// Internal observable for tests: the node id string of the last node that was
    /// drawn as the "executing" node (gold outline). Null if no node was highlighted.
    /// Reset at the start of each Render() call.
    /// </summary>
    internal string? LastExecutingNodeId { get; private set; }

    /// <summary>
    /// Internal observable for tests: the node id string of the last node that was
    /// drawn as the "paused at" node (red outline). Null if not paused.
    /// </summary>
    internal string? LastPausedNodeId { get; private set; }

    public void Render(ICanvasRenderContext ctx)
    {
        LastExecutingNodeId = null;
        LastPausedNodeId    = null;

        if (_session is null) return;

        // 1. Gold outline on the most recently executing node.
        var recent = _session.GetRecentNodeHistory(1);
        if (recent.Count > 0)
        {
            var executing = recent[0];
            if (executing.AssetId == Guid.Empty || executing.AssetId == _asset.AssetId)
            {
                var node = FindNode(executing.NodeIdString);
                if (node is not null)
                {
                    LastExecutingNodeId = executing.NodeIdString;
                    var nodePos = new Vector2(node.EditorMetadata.X, node.EditorMetadata.Y);
                    DrawNodeOutline(ctx, nodePos, RunningColor, 2.5f);
                }
            }
        }

        // 2. Red outline on the paused-at node.
        if (_session.IsPaused && _session.PausedAt is not null)
        {
            var bp = _session.PausedAt;
            if (bp.AssetId == _asset.AssetId)
            {
                var node = FindNode(bp.NodeId);
                if (node is not null)
                {
                    LastPausedNodeId = bp.NodeId;
                    var nodePos = new Vector2(node.EditorMetadata.X, node.EditorMetadata.Y);
                    DrawNodeOutline(ctx, nodePos, PausedColor, 3.0f);
                }
            }
        }

        // 3. Dim dots on recently executed nodes (history trail, suppressed at low zoom).
        if (!ctx.IsLowZoom)
        {
            int count = 0;
            foreach (var executed in _session.GetRecentNodeHistory(20))
            {
                if (count >= 10) break; // max 10 history dots
                if (executed.AssetId != Guid.Empty && executed.AssetId != _asset.AssetId)
                    continue;

                var node = FindNode(executed.NodeIdString);
                if (node is null) continue;

                var nodePos = new Vector2(node.EditorMetadata.X, node.EditorMetadata.Y);
                var center = ctx.Viewport.GraphToScreen(
                    nodePos + new Vector2(DefaultNodeSize.X - 10f, 6f + count * 8f));
                float radius = 3f * ctx.Zoom;
                ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(HistoryColor));
                count++;
            }
        }
    }

    private static void DrawNodeOutline(
        ICanvasRenderContext ctx,
        Vector2 nodeCanvasPos,
        Vector4 color,
        float thickness)
    {
        var min = ctx.Viewport.GraphToScreen(nodeCanvasPos);
        var max = ctx.Viewport.GraphToScreen(nodeCanvasPos + DefaultNodeSize);
        ctx.DrawList.AddRect(min, max, ImGui.GetColorU32(color),
            rounding: 4f * ctx.Zoom,
            flags: ImDrawFlags.None,
            thickness: thickness * ctx.Zoom);
    }

    private Node? FindNode(string nodeId)
    {
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
