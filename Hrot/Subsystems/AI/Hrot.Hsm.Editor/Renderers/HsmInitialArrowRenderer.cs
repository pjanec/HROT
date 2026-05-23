using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Custom canvas renderer that draws initial-state arrows for composite states
// and highlights the LCA composite when a transition link is selected.
// Runs in the AfterNodes pass so arrows appear above node bodies.
public sealed class HsmInitialArrowRenderer : ICustomCanvasRenderer
{
    // Gold highlight for the LCA composite when a transition link is selected.
    private static readonly Vector4 LcaHighlightColor = new(1.00f, 0.85f, 0.00f, 0.55f);
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly HsmAsset _asset;

    public HsmInitialArrowRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public string Id => "hsm.initial_state_arrows";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;
    public bool IsActive { get; set; } = true;

    public void Render(ICanvasRenderContext ctx)
    {
        // TODO: draw filled circle + arrow to initial child for each composite state.

        // LCA highlight: when exactly one transition link is selected, outline its LCA.
        foreach (var linkId in ctx.Selection.Links)
        {
            var transition = _asset.FindTransitionByVisualId(linkId.Value);
            if (transition?.Source is null || transition.Target is null) continue;

            var lca = FindLca(_asset, transition.Source, transition.Target);
            // Synthetic root has no visual representation; skip it.
            if (lca == _asset.RootState) continue;

            DrawLcaOutline(ctx, lca);
        }
    }

    private static void DrawLcaOutline(ICanvasRenderContext ctx, StateNode lca)
    {
        var size = lca.SizeOverride ?? DefaultNodeSize;
        var min  = ctx.Viewport.GraphToScreen(lca.Position);
        var max  = ctx.Viewport.GraphToScreen(lca.Position + size);
        ctx.DrawList.AddRect(min, max, ImGui.GetColorU32(LcaHighlightColor),
            rounding: 4f * ctx.Zoom,
            flags: ImDrawFlags.None,
            thickness: 1.5f * ctx.Zoom);
    }

    // Finds the Lowest Common Ancestor (deepest composite that contains both states).
    // Uses root-to-state ancestor path comparison.
    private static StateNode FindLca(HsmAsset asset, StateNode a, StateNode b)
    {
        var aPath = BuildRootToStatePath(a);
        var bPath = BuildRootToStatePath(b);
        StateNode lca = asset.RootState;
        for (int i = 0; i < Math.Min(aPath.Count, bPath.Count); i++)
        {
            if (aPath[i] == bPath[i]) lca = aPath[i];
            else break;
        }
        return lca;
    }

    // Returns the path from the root (inclusive) down to the given state (inclusive).
    private static List<StateNode> BuildRootToStatePath(StateNode state)
    {
        var path = new List<StateNode>();
        var current = (StateNode?)state;
        while (current is not null)
        {
            path.Add(current);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }
}
