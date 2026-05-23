using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

/// <summary>
/// Renders a heatmap fill behind each HSM state node, coloring it by aggregate
/// state-entry frequency. Active only when HeatmapModeActive is true.
/// Blue (cold) -> green -> yellow -> red (hot).
/// </summary>
public sealed class HsmHeatmapRenderer : ICustomCanvasRenderer
{
    // Default state node size when no SizeOverride is set.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly HsmAsset _asset;
    private IHsmDebugSession? _session;
    private bool _heatmapModeActive;

    public HsmHeatmapRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public string Id => "hsm.heatmap";
    public CanvasRenderPass Pass => CanvasRenderPass.BeforeContent;

    /// <summary>When true and a session is attached, heatmap fills are drawn behind state bodies.</summary>
    public bool HeatmapModeActive
    {
        get => _heatmapModeActive;
        set => _heatmapModeActive = value;
    }

    public bool IsActive => _heatmapModeActive && _session is not null;

    /// <summary>Attaches or detaches the debug session used for state-entry counter data.</summary>
    public void SetSession(IHsmDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        var counts = _session?.GetStateEntryCounts(_asset.AssetId);
        if (counts is null || counts.Count == 0) return;

        int maxCount = 0;
        foreach (var kv in counts)
            if (kv.Value > maxCount) maxCount = kv.Value;
        if (maxCount == 0) return;

        // Only draw for states visible on the canvas.
        foreach (var nodeId in ctx.VisibleNodes)
        {
            var state = _asset.FindStateByStableId(nodeId.Value);
            if (state is null) continue;
            // Skip the synthetic root state.
            if (state == _asset.RootState) continue;

            if (!counts.TryGetValue(state.StableId, out var count)) continue;

            float heat = (float)count / maxCount;
            var fillColor = HeatToColor(heat);

            var size = state.SizeOverride ?? DefaultNodeSize;
            var min = ctx.Viewport.GraphToScreen(state.Position);
            var max = ctx.Viewport.GraphToScreen(state.Position + size);
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
