using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.Hsm.Editor.Renderers;

// Draws a visual indicator between states that have an OutputLaneConflict
// (two parallel regions writing to the same command lane).
// Diagnostics are provided externally by the host after each validation run.
// Renders a thin yellow line + "!" warning glyph at the midpoint.
public sealed class HsmRegionConflictsRenderer : ICustomCanvasRenderer
{
    // Default node size used for center computation when no size override exists.
    private static readonly Vector2 DefaultNodeSize = new(120f, 40f);

    private readonly HsmAsset _asset;
    private IReadOnlyList<HsmDiagnostic>? _diagnostics;

    public string Id => "hsm.region_conflicts";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public HsmRegionConflictsRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public void SetDiagnostics(IReadOnlyList<HsmDiagnostic>? diagnostics) => _diagnostics = diagnostics;

    public void Render(ICanvasRenderContext ctx)
    {
        if (_diagnostics is null || _diagnostics.Count == 0) return;

        var lineColor = new Vector4(1f, 0.8f, 0f, 0.8f);
        var textColor = new Vector4(1f, 0.8f, 0f, 1f);
        uint lineColorU32 = ImGui.GetColorU32(lineColor);
        uint textColorU32 = ImGui.GetColorU32(textColor);
        float thickness = 1.5f * ctx.Zoom;

        foreach (var diag in _diagnostics)
        {
            if (diag.Code != HsmDiagnosticCode.OutputLaneConflict) continue;
            if (diag.TargetStableIds.Count < 2) continue;

            var stateA = _asset.FindStateByStableId(diag.TargetStableIds[0]);
            if (stateA is null) continue;

            var stateB = _asset.FindStateByStableId(diag.TargetStableIds[1]);
            if (stateB is null) continue;

            var sizeA = stateA.SizeOverride ?? DefaultNodeSize;
            var sizeB = stateB.SizeOverride ?? DefaultNodeSize;

            var centerA = ctx.Viewport.GraphToScreen(stateA.Position + sizeA * 0.5f);
            var centerB = ctx.Viewport.GraphToScreen(stateB.Position + sizeB * 0.5f);

            ctx.DrawList.AddLine(centerA, centerB, lineColorU32, thickness);

            var mid = (centerA + centerB) * 0.5f;
            ctx.DrawList.AddText(mid, textColorU32, "!");
        }
    }
}
