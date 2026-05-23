using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.Hsm.Editor.Renderers;

// Renders a small red filled circle in the gutter of each state or transition
// that has an active breakpoint set on it.
// State breakpoints: ElementId == StateNode.StableId
// Transition breakpoints: ElementId == TransitionNode.VisualId (no rendering for now;
//   transition breakpoint rendering is TODO - just state gutter for this slice).
public sealed class HsmBreakpointGutterRenderer : ICustomCanvasRenderer
{
    private readonly HsmAsset _asset;
    private IHsmDebugSession? _session;

    public string Id => "hsm.breakpoint_gutter";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public HsmBreakpointGutterRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public void SetSession(IHsmDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        if (_session is null) return;

        var breakpoints = _session.GetBreakpoints();
        foreach (var bp in breakpoints)
        {
            if (!bp.Enabled) continue;
            if (bp.AssetId != _asset.AssetId) continue;

            var state = _asset.FindStateByStableId(bp.ElementId);
            if (state is null) continue;

            var screenPos = ctx.Viewport.GraphToScreen(state.Position);
            var center = screenPos + new Vector2(-8f, 8f) * ctx.Zoom;
            float radius = 5f * ctx.Zoom;
            var color = new Vector4(0.9f, 0.15f, 0.15f, 1.0f);

            ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(color));
        }
    }
}
