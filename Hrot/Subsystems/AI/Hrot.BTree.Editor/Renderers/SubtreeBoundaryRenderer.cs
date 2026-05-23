using System;
using System.Numerics;
using ImGuiNET;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.BTree.Editor.Renderers;

// Draws a faint blue dashed rectangle around the current subtree root's AABB
// when the debug session is paused inside a subtree (StackPointer > 0).
// Rendered at the BeforeContent pass so it appears behind node bodies.
public sealed class SubtreeBoundaryRenderer : ICustomCanvasRenderer
{
    // Default node size used for AABB computation when no size override exists.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly BehaviorTreeAsset _asset;
    private IBTreeDebugSession? _session;

    public string Id => "btree.subtree_boundaries";
    public CanvasRenderPass Pass => CanvasRenderPass.BeforeContent;
    public bool IsActive => _session?.IsAttached == true;

    public SubtreeBoundaryRenderer(BehaviorTreeAsset asset)
    {
        _asset = asset;
    }

    public void SetSession(IBTreeDebugSession? session) => _session = session;

    public void Render(ICanvasRenderContext ctx)
    {
        var snapshot = _session?.GetCurrentStateSnapshot();
        if (snapshot is null || snapshot.StackPointer == 0) return;

        var rootId = snapshot.StackElementIds?[0];
        if (rootId is null) return;

        var rootNode = _asset.FindNode(rootId.Value);
        if (rootNode is null) return;

        var aabbMin = rootNode.Position;
        var aabbMax = rootNode.Position + DefaultNodeSize;
        ExpandAabb(rootNode, ref aabbMin, ref aabbMax);

        var padding = new Vector2(20f, 20f);
        var minS = ctx.Viewport.GraphToScreen(aabbMin - padding);
        var maxS = ctx.Viewport.GraphToScreen(aabbMax + padding);

        var color = new Vector4(0.3f, 0.5f, 1.0f, 0.3f);
        uint colorU32 = ImGui.GetColorU32(color);
        float thickness = 1.5f * ctx.Zoom;

        DrawDashedRect(ctx.DrawList, minS, maxS, colorU32, 8f, 6f, thickness);
    }

    private void ExpandAabb(BTreeEditorNode node, ref Vector2 aabbMin, ref Vector2 aabbMax)
    {
        foreach (var childId in node.ChildVisualIds)
        {
            var child = _asset.FindNode(childId);
            if (child is null) continue;

            var childMin = child.Position;
            var childMax = child.Position + DefaultNodeSize;

            if (childMin.X < aabbMin.X) aabbMin.X = childMin.X;
            if (childMin.Y < aabbMin.Y) aabbMin.Y = childMin.Y;
            if (childMax.X > aabbMax.X) aabbMax.X = childMax.X;
            if (childMax.Y > aabbMax.Y) aabbMax.Y = childMax.Y;

            ExpandAabb(child, ref aabbMin, ref aabbMax);
        }
    }

    private static void DrawDashedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color, float dashLen, float gapLen, float thickness)
    {
        DrawDashedLine(dl, min,                       new Vector2(max.X, min.Y), color, dashLen, gapLen, thickness);
        DrawDashedLine(dl, new Vector2(max.X, min.Y), max,                       color, dashLen, gapLen, thickness);
        DrawDashedLine(dl, max,                       new Vector2(min.X, max.Y), color, dashLen, gapLen, thickness);
        DrawDashedLine(dl, new Vector2(min.X, max.Y), min,                       color, dashLen, gapLen, thickness);
    }

    private static void DrawDashedLine(ImDrawListPtr dl, Vector2 a, Vector2 b, uint color, float dashLen, float gapLen, float thickness)
    {
        float totalLen = Vector2.Distance(a, b);
        if (totalLen < 0.01f) return;
        var dir = (b - a) / totalLen;
        float offset = 0f;
        bool solid = true;
        while (offset < totalLen)
        {
            float segLen = solid ? dashLen : gapLen;
            float segEnd = Math.Min(offset + segLen, totalLen);
            if (solid)
                dl.AddLine(a + dir * offset, a + dir * segEnd, color, thickness);
            offset = segEnd;
            solid = !solid;
        }
    }
}
