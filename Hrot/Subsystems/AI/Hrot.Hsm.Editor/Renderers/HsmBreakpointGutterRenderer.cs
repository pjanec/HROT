using System.Numerics;
using ImGuiNET;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Renders a small red filled circle in the gutter of each state or transition
// that has an active breakpoint set on it.
// State breakpoints: ElementId == StateNode.StableId
// Transition breakpoints: ElementId == TransitionNode.VisualId
public sealed class HsmBreakpointGutterRenderer : ICustomCanvasRenderer
{
    private readonly HsmAsset _asset;
    private IHsmDebugSession? _session;
    private IDataBreakpointManager? _manager;

    // Counters exposed for tests to verify render output without ImGui.
    // Both are incremented BEFORE the geometry TryGet gate so count-based
    // tests pass even when TryGet returns false (e.g. stub render contexts).
    internal int LastStateDotCount;
    internal int LastTransitionDotCount;

    public string Id => "hsm.breakpoint_gutter";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public HsmBreakpointGutterRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public void SetSession(IHsmDebugSession? session) => _session = session;

    public void SetManager(IDataBreakpointManager? manager) => _manager = manager;

    // Counts (but does not draw) breakpoints for this asset.
    // Exposed for test use; mirrors the categorisation logic in Render().
    // Includes both session breakpoints and manager breakpoints with SourceElementId.
    internal (int StateDots, int TransitionDots) CountBreakpoints()
    {
        if (_asset is null) return (0, 0);

        int stateDots = 0, transDots = 0;

        if (_session is not null)
        {
            foreach (var bp in _session.GetBreakpoints())
            {
                if (!bp.Enabled) continue;
                if (bp.AssetId != _asset.AssetId) continue;

                if (_asset.FindStateByStableId(bp.ElementId) is not null)
                    stateDots++;
                else if (_asset.FindTransitionByVisualId(bp.ElementId) is not null)
                    transDots++;
            }
        }

        if (_manager is not null)
        {
            foreach (var bp in _manager.AllBreakpoints)
            {
                if (!bp.Enabled) continue;
                if (bp.SourceElementId is null) continue;

                if (_asset.FindStateByStableId(bp.SourceElementId.Value) is not null)
                    stateDots++;
                else if (_asset.FindTransitionByVisualId(bp.SourceElementId.Value) is not null)
                    transDots++;
            }
        }

        return (stateDots, transDots);
    }

    public void Render(ICanvasRenderContext ctx)
    {
        LastStateDotCount      = 0;
        LastTransitionDotCount = 0;

        if (_asset is null) return; // sentinel state: canvas not yet opened

        if (_session is not null)
        {
            var breakpoints = _session.GetBreakpoints();
            foreach (var bp in breakpoints)
            {
                if (!bp.Enabled) continue;
                if (bp.AssetId != _asset.AssetId) continue;

                var state = _asset.FindStateByStableId(bp.ElementId);
                if (state is null)
                {
                    var trans = _asset.FindTransitionByVisualId(bp.ElementId);
                    if (trans is null) continue;

                    // Count eligible transition breakpoints BEFORE geometry gate.
                    LastTransitionDotCount++;

                    // Anchor off source state screen rect; fall back to target if source fails.
                    Vector2 dotCenter;
                    if (ctx.TryGetNodeScreenRect(new NodeId(trans.Source.StableId), out var srcRect))
                    {
                        dotCenter = srcRect.Min + new Vector2(-8f, 8f) * ctx.Zoom;
                    }
                    else if (ctx.TryGetNodeScreenRect(new NodeId(trans.Target.StableId), out var tgtRect))
                    {
                        dotCenter = tgtRect.Min + new Vector2(-8f, 8f) * ctx.Zoom;
                    }
                    else
                    {
                        continue;
                    }

                    float radius = 5f * ctx.Zoom;
                    ctx.DrawList.AddCircleFilled(dotCenter, radius,
                        ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.15f, 1.0f)));
                    continue;
                }

                // Count eligible state breakpoints BEFORE geometry gate.
                LastStateDotCount++;

                // Anchor off canvas-computed screen geometry. Skip drawing if not laid out.
                if (!ctx.TryGetNodeScreenRect(new NodeId(state.StableId), out var stateRect))
                    continue;

                // Place dot at top-left corner of the node (with a small offset).
                var stateCenter = stateRect.Min + new Vector2(-8f, 8f) * ctx.Zoom;
                float stateRadius = 5f * ctx.Zoom;
                var color = new Vector4(0.9f, 0.15f, 0.15f, 1.0f);

                ctx.DrawList.AddCircleFilled(stateCenter, stateRadius, ImGui.GetColorU32(color));
            }
        }

        if (_manager is not null)
        {
            foreach (var bp in _manager.AllBreakpoints)
            {
                if (!bp.Enabled) continue;
                if (bp.SourceElementId is null) continue;

                var state = _asset.FindStateByStableId(bp.SourceElementId.Value);
                if (state is not null)
                {
                    // Count eligible state breakpoints BEFORE geometry gate.
                    LastStateDotCount++;

                    // Anchor off canvas-computed screen geometry. Skip drawing if not laid out.
                    if (!ctx.TryGetNodeScreenRect(new NodeId(state.StableId), out var stateRect))
                        continue;

                    var stateCenter = stateRect.Min + new Vector2(-8f, 8f) * ctx.Zoom;
                    float stateRadius = 5f * ctx.Zoom;
                    ctx.DrawList.AddCircleFilled(stateCenter, stateRadius,
                        ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.15f, 1.0f)));
                    continue;
                }

                var trans = _asset.FindTransitionByVisualId(bp.SourceElementId.Value);
                if (trans is not null)
                {
                    // Count eligible transition breakpoints BEFORE geometry gate.
                    LastTransitionDotCount++;

                    // Anchor off source state screen rect; fall back to target if source fails.
                    Vector2 dotCenter;
                    if (ctx.TryGetNodeScreenRect(new NodeId(trans.Source.StableId), out var srcRect))
                    {
                        dotCenter = srcRect.Min + new Vector2(-8f, 8f) * ctx.Zoom;
                    }
                    else if (ctx.TryGetNodeScreenRect(new NodeId(trans.Target.StableId), out var tgtRect))
                    {
                        dotCenter = tgtRect.Min + new Vector2(-8f, 8f) * ctx.Zoom;
                    }
                    else
                    {
                        continue;
                    }

                    float radius = 5f * ctx.Zoom;
                    ctx.DrawList.AddCircleFilled(dotCenter, radius,
                        ImGui.GetColorU32(new Vector4(0.9f, 0.15f, 0.15f, 1.0f)));
                }
            }
        }
    }
}
