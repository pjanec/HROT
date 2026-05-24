using System.Numerics;
using ImGuiNET;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using Hrot.Diagnostics.Breakpoints;
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
    private IDataBreakpointManager? _manager;

    public string Id => "btree.breakpoint_gutter";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public BTreeBreakpointGutterRenderer(BehaviorTreeAsset asset)
    {
        _asset = asset;
    }

    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void SetManager(IDataBreakpointManager? manager) => _manager = manager;

    // Counts (without drawing) how many manager breakpoints have a SourceElementId
    // that maps to a node in this asset. Used by tests.
    internal int CountManagerBreakpoints()
    {
        if (_manager is null) return 0;
        int count = 0;
        foreach (var bp in _manager.AllBreakpoints)
        {
            if (!bp.Enabled) continue;
            if (bp.SourceElementId is null) continue;
            if (_asset.FindNode(bp.SourceElementId.Value) is not null)
                count++;
        }
        return count;
    }

    public void Render(ICanvasRenderContext ctx)
    {
        if (_session is not null)
        {
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

        if (_manager is not null)
        {
            foreach (var bp in _manager.AllBreakpoints)
            {
                if (!bp.Enabled) continue;
                if (bp.SourceElementId is null) continue;

                var node = _asset.FindNode(bp.SourceElementId.Value);
                if (node is null) continue;

                var screenPos = ctx.Viewport.GraphToScreen(node.Position);
                var center = screenPos + new Vector2(-8f, 8f) * ctx.Zoom;
                float radius = 5f * ctx.Zoom;
                var color = new Vector4(0.9f, 0.15f, 0.15f, 1.0f);

                ctx.DrawList.AddCircleFilled(center, radius, ImGui.GetColorU32(color));
            }
        }
    }
}
