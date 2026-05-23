using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

/// <summary>
/// Custom canvas renderer that draws a runtime overlay when an HSM debug session is active.
/// Renders:
///   - Active-configuration glow on every active leaf state and its ancestors.
///   - A pulse marker at the most recently fired transition.
/// Registered at the AfterNodes pass so overlays appear above state bodies.
/// </summary>
public sealed class HsmRuntimeOverlayRenderer : ICustomCanvasRenderer
{
    // Active state glow color (teal).
    private static readonly Vector4 ActiveLeafColor    = new(0.20f, 0.90f, 0.70f, 0.90f);
    // Ancestor dim glow color.
    private static readonly Vector4 AncestorBase       = new(0.20f, 0.90f, 0.70f, 0.45f);
    // Transition pulse marker color (gold).
    private static readonly Vector4 TransitionPulse    = new(1.00f, 0.85f, 0.10f, 0.80f);

    // Default state size used when SizeOverride is not set.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly HsmAsset _asset;
    private IHsmDebugSession? _session;

    public HsmRuntimeOverlayRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public string Id   => "hsm.runtime_overlay";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    /// <summary>Attaches or detaches the debug session used for overlay data.</summary>
    public void SetSession(IHsmDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        var snapshot = _session?.GetCurrentStateSnapshot();
        if (snapshot is null) return;

        // 1. Active-configuration glow on every active leaf and its ancestors.
        foreach (var leafStableId in snapshot.ActiveLeafStableIds)
        {
            var leaf = _asset.FindStateByStableId(leafStableId);
            if (leaf is null) continue;

            // Glow the leaf at full intensity.
            DrawStateOutline(ctx, leaf, ActiveLeafColor, 2.5f);

            // Glow ancestors with diminishing intensity.
            var ancestor = leaf.Parent;
            int depth = 1;
            while (ancestor is not null && ancestor.Parent is not null)
            {
                float alpha = AncestorBase.W / (1 + depth * 0.5f);
                DrawStateOutline(ctx, ancestor, AncestorBase with { W = alpha }, 1.5f);
                ancestor = ancestor.Parent;
                depth++;
            }
        }

        // 2. Recent-transition pulse marker (skip at low zoom).
        if (ctx.IsLowZoom) return;
        var history = _session!.GetRecentTraceHistory(20);
        var lastFired = history.OfType<HsmTransitionFired>().LastOrDefault();
        if (lastFired is null) return;

        var srcState = _asset.FindStateByStableId(lastFired.SourceStateStableId);
        if (srcState is not null)
        {
            var srcSize = srcState.SizeOverride ?? DefaultNodeSize;
            var midGraph = srcState.Position + srcSize * 0.5f;
            var midScreen = ctx.Viewport.GraphToScreen(midGraph);
            // Draw a small pulsing diamond marker at the source state center.
            float r = 6f * ctx.Zoom;
            ctx.DrawList.AddNgonFilled(midScreen, r, ImGui.GetColorU32(TransitionPulse), 4);
        }
    }

    private static void DrawStateOutline(
        ICanvasRenderContext ctx,
        StateNode state,
        Vector4 color,
        float thickness)
    {
        var size = state.SizeOverride ?? DefaultNodeSize;
        var min  = ctx.Viewport.GraphToScreen(state.Position);
        var max  = ctx.Viewport.GraphToScreen(state.Position + size);
        ctx.DrawList.AddRect(min, max, ImGui.GetColorU32(color),
            rounding: 4f * ctx.Zoom,
            flags: ImDrawFlags.None,
            thickness: thickness * ctx.Zoom);
    }
}
