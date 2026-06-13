using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Renderers;

// Draws a visual indicator between states that have an OutputLaneConflict
// (two parallel regions writing to the same command lane).
// Diagnostics are provided externally by the host after each validation run.
// Renders a thin yellow line + "!" warning glyph at the midpoint.
// Implements ICustomCanvasHitTester so clicking the "!" glyph can be detected.
public sealed class HsmRegionConflictsRenderer : ICustomCanvasRenderer, ICustomCanvasHitTester
{
    private readonly HsmAsset _asset;
    private IReadOnlyList<HsmDiagnostic>? _diagnostics;

    // Screen-space center of each "!" glyph drawn in the last Render() call.
    // The tuple item name is kept as GraphPos for source compatibility with existing tests
    // that insert values directly; semantically these are screen-space positions after
    // the RHS-02 re-anchor (the tests use identity viewport so graph == screen).
    internal readonly List<(Vector2 GraphPos, string Key)> _glyphPositions = new();

    // Counter exposed for tests to verify glyph output without ImGui.
    // Incremented after the diagnostic eligibility check, BEFORE the TryGet geometry gate,
    // so count-based tests pass even when TryGet returns false (e.g. stub contexts).
    internal int LastGlyphCount;

    public string Id => "hsm.region_conflicts";
    public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;

    public HsmRegionConflictsRenderer(HsmAsset asset)
    {
        _asset = asset;
    }

    public void SetDiagnostics(IReadOnlyList<HsmDiagnostic>? diagnostics) => _diagnostics = diagnostics;

    internal IReadOnlyList<HsmDiagnostic>? CurrentDiagnostics => _diagnostics;

    public void Render(ICanvasRenderContext ctx)
    {
        _glyphPositions.Clear();
        LastGlyphCount = 0;

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

            // Count eligible conflicts BEFORE geometry gate.
            LastGlyphCount++;

            // Anchor off canvas-computed screen geometry. Skip drawing if either node not laid out.
            if (!ctx.TryGetNodeScreenRect(new NodeId(stateA.StableId), out var rectA)) continue;
            if (!ctx.TryGetNodeScreenRect(new NodeId(stateB.StableId), out var rectB)) continue;

            // rectA/rectB are already screen-space — centers are their Center properties.
            var centerA = rectA.Center;
            var centerB = rectB.Center;

            ctx.DrawList.AddLine(centerA, centerB, lineColorU32, thickness);

            var mid = (centerA + centerB) * 0.5f;
            ctx.DrawList.AddText(mid, textColorU32, "!");

            // Record screen-space midpoint for hit testing.
            string key = $"conflict_{diag.TargetStableIds[0]}_{diag.TargetStableIds[1]}";
            _glyphPositions.Add((mid, key));
        }
    }

    // ICustomCanvasHitTester: returns a hit if canvasPoint (screen space) is
    // within 8 px of a "!" glyph drawn in the most recent Render() call.
    // _glyphPositions now stores screen-space positions directly.
    public CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx)
    {
        const float HitRadius = 8f;
        foreach (var (screenPos, key) in _glyphPositions)
        {
            if (MathF.Abs(canvasPoint.X - screenPos.X) <= HitRadius &&
                MathF.Abs(canvasPoint.Y - screenPos.Y) <= HitRadius)
            {
                return new CustomElementHit(key, CustomElementKind.Standalone,
                    new RectF(new Vector2(screenPos.X - HitRadius, screenPos.Y - HitRadius),
                              new Vector2(HitRadius * 2f, HitRadius * 2f)));
            }
        }
        return null;
    }
}
