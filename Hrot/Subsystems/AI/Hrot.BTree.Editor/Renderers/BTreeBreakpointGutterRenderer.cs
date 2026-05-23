using System.Numerics;
using ImGuiNET;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.BTree.Editor.Renderers;

// Renders a small red filled circle in the node gutter for each node
// that has an active breakpoint set on it in the current debug session.
// Active breakpoints are those where Breakpoint.ElementId == node.VisualId
// and Breakpoint.Enabled is true.
public sealed class BTreeBreakpointGutterRenderer : ICustomCanvasRenderer
{
    private readonly BehaviorTreeAsset _asset;
    private IBTreeDebugSession? _session;

    public string Id => "btree.breakpoint_gutter";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public BTreeBreakpointGutterRenderer(BehaviorTreeAsset asset)
    {
        _asset = asset;
    }

    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        if (_session is null) return;

        var breakpoints = _session.GetBreakpoints();
        foreach (var bp in breakpoints)
        {
            if (!bp.Enabled) continue;
            if (bp.AssetId != _asset.AssetId) continue;

            var node = _asset.FindNode(bp.ElementId);
            if (node is null) continue;

            var screenPos = ctx.Viewport.GraphToScreen(node.Position);
            var center = screenPos + new Vector2(-8f, 8f) * ctx.Zoom;
            float radius = 5f * ctx.Zoom;
            var color = new Vector4(0.9f, 0.15f, 0.15f, 1.0f);

            ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(color));
        }
    }
}
