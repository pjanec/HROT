using System.Numerics;
using ImGuiNET;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Renderers;

/// <summary>
/// Renders a heatmap overlay behind each BTree node, coloring it by aggregate
/// execution frequency. Active only when HeatmapModeActive is true.
/// Blue (cold, rarely executed) -> green -> yellow -> red (hot, frequently executed).
/// </summary>
public sealed class HeatmapOverlayRenderer : ICustomCanvasRenderer
{
    // Default node size used when the node has no explicit size in the asset.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly BehaviorTreeAsset _asset;
    private IBTreeDebugSession? _session;
    private bool _heatmapModeActive;

    public HeatmapOverlayRenderer(BehaviorTreeAsset asset)
    {
        _asset = asset;
    }

    public string Id => "btree.heatmap_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.BeforeContent;

    /// <summary>When true and a session is attached, heatmap fills are drawn behind nodes.</summary>
    public bool HeatmapModeActive
    {
        get => _heatmapModeActive;
        set => _heatmapModeActive = value;
    }

    public bool IsActive => _heatmapModeActive && _session is not null;

    /// <summary>Attaches or detaches the debug session used for aggregate counter data.</summary>
    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        var aggregates = _session?.GetAggregateCounters(_asset.AssetId);
        if (aggregates is null || aggregates.Count == 0) return;

        int maxCount = 0;
        foreach (var kv in aggregates)
            if (kv.Value > maxCount) maxCount = kv.Value;
        if (maxCount == 0) return;

        foreach (var nodeId in ctx.VisibleNodes)
        {
            var node = _asset.FindNode(nodeId.Value);
            if (node is null) continue;

            if (!aggregates.TryGetValue(node.VisualId, out var count)) continue;

            float heat = (float)count / maxCount;
            var fillColor = HeatToColor(heat);

            var min = ctx.Viewport.GraphToScreen(node.Position);
            var max = ctx.Viewport.GraphToScreen(node.Position + DefaultNodeSize);
            ctx.DrawList.AddRectFilled(min, max, ImGui.GetColorU32(fillColor),
                rounding: 4f * ctx.Zoom);
        }
    }

    // Maps heat in [0,1] to a blue->green->yellow->red gradient with alpha 0.45.
    private static Vector4 HeatToColor(float heat)
    {
        const float alpha = 0.45f;
        if (heat <= 0.33f)
        {
            float t = heat / 0.33f;
            return Vector4.Lerp(
                new Vector4(0.0f, 0.2f, 1.0f, alpha),
                new Vector4(0.0f, 1.0f, 0.2f, alpha), t);
        }
        if (heat <= 0.67f)
        {
            float t = (heat - 0.33f) / 0.34f;
            return Vector4.Lerp(
                new Vector4(0.0f, 1.0f, 0.2f, alpha),
                new Vector4(1.0f, 1.0f, 0.0f, alpha), t);
        }
        else
        {
            float t = (heat - 0.67f) / 0.33f;
            return Vector4.Lerp(
                new Vector4(1.0f, 1.0f, 0.0f, alpha),
                new Vector4(1.0f, 0.1f, 0.0f, alpha), t);
        }
    }
}
