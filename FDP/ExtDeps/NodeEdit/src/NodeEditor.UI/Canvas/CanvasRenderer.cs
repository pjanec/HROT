using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Find;
using NodeEditor.UI.Util;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Top-level canvas renderer. Orchestrates the full per-frame pipeline:
/// layout build → hit-test → input handling → draw phases (grid, comments-back,
/// wires, nodes, comments-front, pending wire, marquee).
///
/// Usage: create once and call <c>Render</c> every ImGui frame while the
/// canvas child window context is active.
/// </summary>
public sealed class CanvasRenderer
{
    private readonly CanvasLayoutBuilder _layoutBuilder = new();
    private readonly CanvasLayout        _layout        = new();
    private readonly SpatialIndex        _spatialIndex  = new();
    private readonly HitTester           _hitTester     = new();
    private readonly CanvasInput         _input         = new();
    private readonly GridRenderer        _grid          = new();
    private readonly WireRenderer        _wires         = new();
    private readonly NodeRenderer        _nodes         = new();

    /// <summary>
    /// Render one frame of the node-editor canvas. Call this inside an ImGui window
    /// (not inside an existing child window). The method opens and closes its own
    /// child window to establish a clip/scroll region.
    /// </summary>
    /// <param name="view">The graph view to render.</param>
    public void Render(GraphView view)
    {
        Render(view, findBar: null);
    }

    /// <summary>
    /// Render one frame of the node-editor canvas, optionally drawing a find overlay.
    /// The find bar (if visible) is drawn as a slim band above the canvas, and matching
    /// nodes receive highlight outlines while non-matching nodes are dimmed.
    /// </summary>
    /// <param name="view">The graph view to render.</param>
    /// <param name="findBar">Optional find bar; overlays are only drawn when <see cref="FindBar.IsVisible"/> is true.</param>
    public void Render(GraphView view, FindBar? findBar)
    {
        // Draw find bar above the canvas
        findBar?.Draw();

        var avail = ImGui.GetContentRegionAvail();
        if (avail.X <= 0 || avail.Y <= 0) return;

        if (!ImGui.BeginChild("##ne_canvas", avail, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.EndChild();
            return;
        }

        try
        {
            RenderInner(view, findBar);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private void RenderInner(GraphView view, FindBar? findBar = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size   = ImGui.GetContentRegionAvail();
        var dl     = ImGui.GetWindowDrawList();

        // Publish canvas bounds so viewport transforms are correct.
        view.Viewport.CanvasScreenOrigin = origin;
        view.Viewport.CanvasScreenSize   = size;

        // Claim the full canvas area as a dummy item (no interaction, just layout).
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        // 1. Build layout (screen rects, pin positions, spatial index).
        _layoutBuilder.Build(view, _layout, _spatialIndex);

        // 2. Hit-test to update hover info.
        _hitTester.UpdateHover(view, _spatialIndex, _layout.PinScreenPositions);

        // 3. Process input.
        _input.Handle(view);

        // ── Draw phases ───────────────────────────────────────────────────

        // 4. Grid + background (also fills the solid background color).
        _grid.Draw(view, dl, origin, size);

        // 5. Comment boxes — background layer (below nodes).
        DrawComments(dl, view, foreground: false);

        // 6. Wires.
        _wires.DrawAll(view, dl, _layout.PinScreenPositions);

        // 7. Nodes + inline editors.
        _nodes.DrawAll(view, dl, _layout.NodeScreenRects, _layout.PinScreenPositions, _layout.ConnectedInputPins);

        // 8. Comment boxes — foreground layer (header text on top of nodes).
        DrawComments(dl, view, foreground: true);

        // 9. Reroute waypoints.
        ReroutesRenderer.Render(view.Model, view.Selection, view.Viewport, view.TypeSystem);

        // 10. Pending wire being dragged.
        DrawPendingWire(view, dl);

        // 11. Marquee selection rectangle.
        DrawMarquee(view, dl);

        // 12. Find overlay (match highlights + dim pass).
        if (findBar?.IsVisible == true && findBar.Results.Count > 0)
            DrawFindOverlay(view, dl, findBar);
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    private static void DrawComments(ImDrawListPtr dl, GraphView view, bool foreground)
    {
        var theme = view.Host.Theme;
        var comments = view.Model.Comments.ToList();
        comments.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

        foreach (var comment in comments)
        {
            var min = view.Viewport.GraphToScreen(comment.Position);
            var max = view.Viewport.GraphToScreen(comment.Position + comment.Size);
            float headerH = 20f * view.Viewport.Zoom;

            bool selected = view.Selection.Contains(SelectionEntry.OfComment(comment.Id));

            if (!foreground)
            {
                // Body fill (semi-transparent)
                var bodyColor = comment.Color with { W = 0.15f };
                dl.AddRectFilled(min, max, ImGui.GetColorU32(bodyColor), 4f);

                // Header strip
                var headerMax = new Vector2(max.X, min.Y + headerH);
                dl.AddRectFilled(min, headerMax, ImGui.GetColorU32(comment.Color with { W = 0.65f }), 4f,
                    ImDrawFlags.RoundCornersTop);

                // Border
                uint borderColor = selected
                    ? ImGui.GetColorU32(theme.SelectionAccent)
                    : ImGui.GetColorU32(comment.Color);
                dl.AddRect(min, max, borderColor, 4f, ImDrawFlags.None, selected ? 2f : 1f);
            }
            else
            {
                // Header text
                uint textColor = ImGui.GetColorU32(theme.TextDefault);
                dl.AddText(min + new Vector2(6f, 4f), textColor, comment.Text.Split('\n')[0]);
            }
        }
    }

    // ── Pending wire ──────────────────────────────────────────────────────────

    private void DrawPendingWire(GraphView view, ImDrawListPtr dl)
    {
        var pw = view.Interaction.PendingWire;
        if (pw == null || view.Interaction.Mode != InteractionMode.PendingWire) return;

        _layout.PinScreenPositions.TryGetValue(pw.SourcePin, out var a);
        if (a == default) a = view.Host.Input.MousePosition;

        Vector2 b = _layout.PinScreenPositions.TryGetValue(
            pw.CandidateTarget ?? default, out var snapPos)
            ? snapPos
            : view.Viewport.GraphToScreen(pw.CursorGraph);

        var srcPin  = view.Model.FindPin(pw.SourcePin);
        bool isExec = srcPin?.Kind == PinKind.Exec;

        uint wireColor = pw.CandidateTarget.HasValue
            ? pw.CandidateValid
                ? ImGui.GetColorU32(new Vector4(0.3f, 1f, 0.3f, 1f))
                : ImGui.GetColorU32(new Vector4(1f, 0.3f, 0.3f, 1f))
            : ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.85f));

        float thickness = isExec ? view.Host.Theme.WireThicknessExec : view.Host.Theme.WireThicknessData;
        var (c1, c2) = HitTester.WireTangents(a, b);

        if (isExec)
            dl.AddBezierWithArrow(a, c1, c2, b, wireColor, thickness, thickness * 2.5f);
        else
            dl.AddBezierCubic(a, c1, c2, b, wireColor, thickness);
    }

    // ── Marquee ───────────────────────────────────────────────────────────────

    private static void DrawMarquee(GraphView view, ImDrawListPtr dl)
    {
        if (view.Interaction.Mode != InteractionMode.MarqueeSelecting) return;

        var marquee = view.Interaction.MarqueeGraph;
        var min = view.Viewport.GraphToScreen(marquee.Min);
        var max = view.Viewport.GraphToScreen(marquee.Min + marquee.Size);

        var theme = view.Host.Theme;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(theme.SelectionAccent with { W = 0.1f }));
        dl.AddRect(min, max, ImGui.GetColorU32(theme.SelectionAccent), 0f, ImDrawFlags.None, 1.5f);
    }

    // ── Find overlay ─────────────────────────────────────────────────────────

    private static void DrawFindOverlay(GraphView view, ImDrawListPtr dl, FindBar findBar)
    {
        var matchNodeIds = new HashSet<NodeId>();
        foreach (var r in findBar.Results)
            if (r.Node.HasValue) matchNodeIds.Add(r.Node.Value);

        // Dim non-matching nodes
        foreach (var node in view.Model.Nodes)
        {
            if (matchNodeIds.Contains(node.Id)) continue;
            var pos  = node.Position;
            var size = node.SizeOverride ?? new Vector2(160, 64);
            var min  = view.Viewport.GraphToScreen(pos);
            var max  = view.Viewport.GraphToScreen(pos + size);
            dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 4f);
        }

        // Yellow outline for matching nodes
        for (int i = 0; i < findBar.Results.Count; i++)
        {
            var result = findBar.Results[i];
            if (!result.Node.HasValue) continue;
            var node = view.Model.FindNode(result.Node.Value);
            if (node is null) continue;

            var size  = node.SizeOverride ?? new Vector2(160, 64);
            var min   = view.Viewport.GraphToScreen(node.Position);
            var max   = view.Viewport.GraphToScreen(node.Position + size);
            bool isActive = (i == findBar.ActiveIndex);

            var outlineColor = isActive
                ? new Vector4(1f, 0.9f, 0.1f, 1f)
                : new Vector4(1f, 0.85f, 0.0f, 0.7f);
            float thickness = isActive ? 3.0f : 1.5f;
            dl.AddRect(min, max, ImGui.GetColorU32(outlineColor), 4f, ImDrawFlags.None, thickness);
        }
    }
}
